// .NET 8 marks its QUIC classes as a preview. They are used on purpose here.
#pragma warning disable CA2252, CA1416

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

// Lets a web browser connect to this machine with WebTransport, which is how a guest joins from a
// web page. A browser normally refuses a server whose certificate no authority has signed. It
// accepts this one because the page is given the certificate's fingerprint in the join link, and
// a certificate named that way may live for at most two weeks. A new one is made every time.
//
// WebTransport runs inside HTTP/3. Only the part a browser needs is here: the settings exchange
// and the CONNECT request that opens a session. After that every message travels on a stream of
// its own, so a lost packet delays one message and the ones behind it still arrive.
public sealed class WebTransportListener : IDisposable
{
    private const long ControlStream = 0x00;
    private const long SettingsFrame = 0x04;
    private const long HeadersFrame = 0x01;
    internal const long SessionStreamOneWay = 0x54;
    private const long SessionStreamTwoWay = 0x41;
    private const int LargestRequestBytes = 16 * 1024;
    private const int LargestMessageBytes = 64 * 1024;
    private const int CertificateDays = 10;

    private readonly Action<string> log;
    private readonly CancellationTokenSource stopping = new CancellationTokenSource();
    private readonly X509Certificate2 certificate;
    private readonly ConcurrentDictionary<QuicConnection, byte> connections = new ConcurrentDictionary<QuicConnection, byte>();
    private QuicListener listener;

    public int Port { get; }

    // The certificate's SHA-256 fingerprint in URL-safe base64, as the join link carries it.
    public string CertificateFingerprint { get; }

    // Raised on a worker thread when a browser has opened a session.
    public event Action<WebTransportSession> SessionOpened;

    // Windows 11, or Linux with libmsquic installed.
    public static bool IsSupported => QuicListener.IsSupported;

    public WebTransportListener(int port, Action<string> log)
    {
        Port = port;
        this.log = log;
        certificate = MakeCertificate();

        if (!QuicListener.IsSupported)
        {
            throw new PlatformNotSupportedException("QUIC is not available on this machine.");
        }

        if (!TryAnnounceDatagrams(out string problem))
        {
            log?.Invoke($"[Stream] Firefox will not be able to join: {problem}");
        }

        CertificateFingerprint = Convert.ToBase64String(SHA256.HashData(certificate.RawData)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _ = Task.Run(Listen);
    }

    private static X509Certificate2 MakeCertificate()
    {
        // Browsers accept a fingerprint only for this kind of key.
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=RomBox", key, HashAlgorithmName.SHA256);
        // A certificate with no extensions is written in the old version 1 form, which browsers
        // refuse to read. The name is never checked; the fingerprint is.
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("rombox");
        request.CertificateExtensions.Add(names.Build());
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 made = request.CreateSelfSigned(now.AddHours(-1), now.AddDays(CertificateDays));

        // Windows can only use a key that sits in its key store; loading the certificate from
        // PFX data puts it there for as long as the certificate object lives.
        return OperatingSystem.IsWindows() ? new X509Certificate2(made.Export(X509ContentType.Pfx)) : new X509Certificate2(made.Export(X509ContentType.Pfx), (string)null, X509KeyStorageFlags.Exportable);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MsQuicOpenVersion(uint version, out IntPtr api);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MsQuicSetParam(IntPtr handle, uint parameter, uint length, IntPtr value);

    private static bool announcesDatagrams;

    // Firefox opens a session only with a server whose QUIC handshake says it accepts datagrams.
    // .NET has no switch for that, so it is set in MsQuic, the library underneath, as a default
    // for every connection in this process. Nothing here reads the datagrams a browser sends.
    // The numbers are from msquic.h: the function table from MsQuicOpenVersion has SetParam
    // fourth, and QUIC_SETTINGS keeps the "is set" bit for DatagramReceiveEnabled at bit 27 and
    // the value at bit 3 of byte 106.
    private static bool TryAnnounceDatagrams(out string problem)
    {
        problem = null;

        if (announcesDatagrams)
        {
            return true;
        }

        try
        {
            // .NET has loaded MsQuic by the time IsSupported answers true. The same copy is
            // looked up among the loaded libraries so the setting lands where .NET will read it.
            string path = null;

            foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            {
                string name = module.ModuleName?.ToLowerInvariant() ?? "";

                if (name == "msquic.dll" || name.StartsWith("libmsquic."))
                {
                    path = module.FileName;
                    break;
                }
            }

            if (path == null)
            {
                problem = "MsQuic was not found among the loaded libraries.";
                return false;
            }

            IntPtr library = NativeLibrary.Load(path);
            var open = Marshal.GetDelegateForFunctionPointer<MsQuicOpenVersion>(NativeLibrary.GetExport(library, "MsQuicOpenVersion"));
            int status = open(2, out IntPtr api);

            if (status != 0 || api == IntPtr.Zero)
            {
                problem = $"MsQuic did not open (status 0x{status:x}).";
                return false;
            }

            const uint GlobalSettings = 0x01000005;
            const int SettingsSize = 144;
            var setParameter = Marshal.GetDelegateForFunctionPointer<MsQuicSetParam>(Marshal.ReadIntPtr(api, 3 * IntPtr.Size));
            IntPtr settings = Marshal.AllocHGlobal(SettingsSize);

            try
            {
                Marshal.Copy(new byte[SettingsSize], 0, settings, SettingsSize);
                Marshal.WriteByte(settings, 3, 0x08);
                Marshal.WriteByte(settings, 106, 0x08);
                status = setParameter(IntPtr.Zero, GlobalSettings, SettingsSize, settings);
            }
            finally
            {
                Marshal.FreeHGlobal(settings);
            }

            if (status != 0)
            {
                problem = $"MsQuic refused the setting (status 0x{status:x}).";
                return false;
            }

            // The library stays open on purpose; closing it could undo the setting.
            announcesDatagrams = true;
            return true;
        }
        catch (Exception failure)
        {
            problem = failure.Message;
            return false;
        }
    }

    private async Task Listen()
    {
        var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 };

        try
        {
            listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, Port),
                ApplicationProtocols = protocols,
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
                {
                    DefaultStreamErrorCode = 0x10c,
                    DefaultCloseErrorCode = 0x100,
                    MaxInboundBidirectionalStreams = 16,
                    MaxInboundUnidirectionalStreams = 200,
                    IdleTimeout = TimeSpan.FromSeconds(15),
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ApplicationProtocols = protocols,
                        ServerCertificate = certificate
                    }
                })
            }, stopping.Token);
        }
        catch (Exception failure)
        {
            log?.Invoke($"[Stream] browsers cannot join: UDP port {Port} did not open ({failure.Message}).");
            return;
        }

        log?.Invoke($"[Stream] browsers can join on UDP port {Port}.");

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                QuicConnection connection = await listener.AcceptConnectionAsync(stopping.Token);
                connections[connection] = 0;
                _ = Task.Run(() => Serve(connection));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception failure)
            {
                // One caller's failed handshake; the listener carries on.
                log?.Invoke($"[Stream] a browser's connection failed to open: {failure.Message}");
            }
        }
    }

    private async Task Serve(QuicConnection connection)
    {
        var sessions = new ConcurrentDictionary<long, WebTransportSession>();
        QuicStream control = null;

        try
        {
            control = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, stopping.Token);
            await control.WriteAsync(BuildSettings(), stopping.Token);
            await control.FlushAsync(stopping.Token);

            while (true)
            {
                QuicStream stream = await connection.AcceptInboundStreamAsync(stopping.Token);
                _ = Task.Run(() => Read(connection, stream, sessions));
            }
        }
        catch (Exception)
        {
            // The browser closed the connection, or RomBox is stopping.
        }
        finally
        {
            // Held until here on purpose: a browser ends the connection if this stream closes.
            GC.KeepAlive(control);

            foreach (WebTransportSession session in sessions.Values)
            {
                session.MarkClosed();
            }

            connections.TryRemove(connection, out _);
            await Discard(connection);
        }
    }

    private static byte[] BuildSettings()
    {
        var settings = new MemoryStream();

        void Add(long id, long value)
        {
            WriteNumber(settings, id);
            WriteNumber(settings, value);
        }

        Add(0x08, 1);            // CONNECT with a protocol, which a session is opened with
        Add(0x33, 1);            // HTTP datagrams; browsers will not open a session without it
        Add(0xffd277, 1);        // the same, as older browsers know it
        Add(0x2b603742, 1);      // WebTransport, as Chromium knows it
        Add(0xc671706a, 4);      // WebTransport sessions allowed, as newer drafts say it

        var message = new MemoryStream();
        WriteNumber(message, ControlStream);
        WriteNumber(message, SettingsFrame);
        WriteNumber(message, settings.Length);
        settings.WriteTo(message);
        return message.ToArray();
    }

    private async Task Read(QuicConnection connection, QuicStream stream, ConcurrentDictionary<long, WebTransportSession> sessions)
    {
        try
        {
            long first = await ReadNumber(stream, stopping.Token);

            if (stream.Type == QuicStreamType.Unidirectional)
            {
                if (first != SessionStreamOneWay)
                {
                    // The browser's own HTTP/3 control streams. They stay open and say nothing needed here.
                    await Drain(stream);
                    return;
                }

                long sessionId = await ReadNumber(stream, stopping.Token);
                byte[] message = await ReadMessage(stream);

                if (message != null && sessions.TryGetValue(sessionId, out WebTransportSession session))
                {
                    session.RaiseMessage(message);
                }

                return;
            }

            if (first == SessionStreamTwoWay)
            {
                await Drain(stream);
                return;
            }

            await ReadRequest(connection, stream, first, sessions);
        }
        catch (Exception)
        {
            // A stream cut short carries nothing usable.
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    // The request stream of a session. It stays open for the session's whole life, and its end
    // is the end of the session.
    private async Task ReadRequest(QuicConnection connection, QuicStream stream, long frameType, ConcurrentDictionary<long, WebTransportSession> sessions)
    {
        WebTransportSession session = null;

        try
        {
            while (true)
            {
                long length = await ReadNumber(stream, stopping.Token);

                if (length > LargestRequestBytes)
                {
                    return;
                }

                byte[] payload = new byte[length];
                await stream.ReadExactlyAsync(payload, stopping.Token);

                if (frameType == HeadersFrame && session == null)
                {
                    // Every request is taken as a request to join. The answer is "200" and the
                    // draft marker older Chromium versions wait for.
                    await stream.WriteAsync(BuildAcceptance(), stopping.Token);
                    await stream.FlushAsync(stopping.Token);
                    session = new WebTransportSession(connection, stream.Id, stopping.Token);
                    sessions[stream.Id] = session;
                    SessionOpened?.Invoke(session);
                }

                frameType = await ReadNumber(stream, stopping.Token);
            }
        }
        finally
        {
            if (session != null)
            {
                sessions.TryRemove(session.Id, out _);
                session.MarkClosed();
            }
        }
    }

    private static byte[] BuildAcceptance()
    {
        byte[] name = Encoding.ASCII.GetBytes("sec-webtransport-http3-draft");
        byte[] value = Encoding.ASCII.GetBytes("draft02");
        var fields = new MemoryStream();
        // Two bytes of header-compression preamble, then ":status 200" by its table number.
        fields.Write(new byte[] { 0x00, 0x00, 0xd9 });
        // A header spelled out in full: marker and name length, name, value length, value.
        fields.WriteByte(0x27);
        fields.WriteByte((byte)(name.Length - 7));
        fields.Write(name);
        fields.WriteByte((byte)value.Length);
        fields.Write(value);

        var message = new MemoryStream();
        WriteNumber(message, HeadersFrame);
        WriteNumber(message, fields.Length);
        fields.WriteTo(message);
        return message.ToArray();
    }

    private async Task<byte[]> ReadMessage(QuicStream stream)
    {
        var message = new MemoryStream();
        byte[] buffer = new byte[2048];

        while (true)
        {
            int read = await stream.ReadAsync(buffer, stopping.Token);

            if (read == 0)
            {
                return message.ToArray();
            }

            if (message.Length + read > LargestMessageBytes)
            {
                return null;
            }

            message.Write(buffer, 0, read);
        }
    }

    private async Task Drain(QuicStream stream)
    {
        byte[] buffer = new byte[1024];

        while (await stream.ReadAsync(buffer, stopping.Token) > 0)
        {
        }
    }

    // QUIC writes numbers in 1, 2, 4 or 8 bytes; the top two bits of the first byte say which.
    internal static void WriteNumber(Stream stream, long value)
    {
        if (value < 0x40)
        {
            stream.WriteByte((byte)value);
        }
        else if (value < 0x4000)
        {
            stream.WriteByte((byte)(0x40 | (value >> 8)));
            stream.WriteByte((byte)value);
        }
        else if (value < 0x40000000)
        {
            stream.WriteByte((byte)(0x80 | (value >> 24)));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
        else
        {
            stream.WriteByte((byte)(0xc0 | (value >> 56)));

            for (int shift = 48; shift >= 0; shift -= 8)
            {
                stream.WriteByte((byte)(value >> shift));
            }
        }
    }

    private static async Task<long> ReadNumber(QuicStream stream, CancellationToken cancellation)
    {
        byte[] bytes = new byte[8];
        await stream.ReadExactlyAsync(bytes.AsMemory(0, 1), cancellation);
        int length = 1 << (bytes[0] >> 6);
        long value = bytes[0] & 0x3f;

        if (length > 1)
        {
            await stream.ReadExactlyAsync(bytes.AsMemory(1, length - 1), cancellation);
        }

        for (int index = 1; index < length; index++)
        {
            value = (value << 8) | bytes[index];
        }

        return value;
    }

    private static async Task Discard(QuicConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (stopping.IsCancellationRequested)
        {
            return;
        }

        stopping.Cancel();
        QuicListener open = listener;
        ICollection<QuicConnection> remaining = connections.Keys;

        // Closing waits on the network, so it happens off the calling thread. The certificate
        // goes last: its key leaves the Windows key store with it.
        _ = Task.Run(async () =>
        {
            foreach (QuicConnection connection in remaining)
            {
                await Discard(connection);
            }

            if (open != null)
            {
                try
                {
                    await open.DisposeAsync();
                }
                catch (Exception)
                {
                }
            }

            certificate.Dispose();
        });
    }
}

// One browser's connection. Messages go out in the order given, each on a stream of its own.
public sealed class WebTransportSession : IStreamGuestLink
{
    // Pictures waiting to leave. More than this means the connection cannot keep up, and waiting
    // would only add delay: pictures are dropped until the next one that stands by itself.
    private const int MostPicturesWaiting = 6;
    private const int MostSoundWaiting = 30;

    private readonly QuicConnection connection;
    private readonly CancellationToken stopping;
    private readonly Channel<(byte[] Message, StreamMessageKind Kind)> outgoing = Channel.CreateUnbounded<(byte[], StreamMessageKind)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly byte[] streamHead;
    private int picturesWaiting;
    private int soundWaiting;
    private int closed;
    private volatile bool waitsForKeyframe;

    internal long Id { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public long PicturesDropped { get; private set; }
    public bool IsClosed => closed != 0;

    // Both raised on worker threads.
    public event Action<byte[]> MessageReceived;
    public event Action Closed;
    // The browser fell behind and needs a picture that stands by itself to carry on.
    public event Action KeyframeNeeded;

    internal WebTransportSession(QuicConnection connection, long id, CancellationToken stopping)
    {
        this.connection = connection;
        this.stopping = stopping;
        Id = id;
        RemoteEndPoint = connection.RemoteEndPoint;

        var head = new MemoryStream();
        WebTransportListener.WriteNumber(head, WebTransportListener.SessionStreamOneWay);
        WebTransportListener.WriteNumber(head, id);
        streamHead = head.ToArray();
        _ = Task.Run(SendLoop);
    }

    // message must not be changed afterwards; it is sent from another thread.
    public void Send(byte[] message, StreamMessageKind kind)
    {
        if (closed != 0)
        {
            return;
        }

        if (kind == StreamMessageKind.Picture || kind == StreamMessageKind.PictureKeyframe)
        {
            bool isBehind = Volatile.Read(ref picturesWaiting) >= MostPicturesWaiting;

            if (isBehind || (waitsForKeyframe && kind == StreamMessageKind.Picture))
            {
                PicturesDropped++;

                if (!waitsForKeyframe)
                {
                    waitsForKeyframe = true;
                    KeyframeNeeded?.Invoke();
                }

                return;
            }

            waitsForKeyframe = false;
            Interlocked.Increment(ref picturesWaiting);
        }
        else if (kind == StreamMessageKind.Sound)
        {
            if (Volatile.Read(ref soundWaiting) >= MostSoundWaiting)
            {
                return;
            }

            Interlocked.Increment(ref soundWaiting);
        }

        outgoing.Writer.TryWrite((message, kind));
    }

    private async Task SendLoop()
    {
        try
        {
            await foreach ((byte[] message, StreamMessageKind kind) in outgoing.Reader.ReadAllAsync(stopping))
            {
                QuicStream stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, stopping);
                byte[] whole = new byte[streamHead.Length + message.Length];
                Buffer.BlockCopy(streamHead, 0, whole, 0, streamHead.Length);
                Buffer.BlockCopy(message, 0, whole, streamHead.Length, message.Length);
                await stream.WriteAsync(whole, completeWrites: true, stopping);

                // Closing a stream waits for the browser to confirm it has everything. The next
                // message must not wait for that.
                _ = Finish(stream);

                if (kind == StreamMessageKind.Sound)
                {
                    Interlocked.Decrement(ref soundWaiting);
                }
                else if (kind != StreamMessageKind.Control)
                {
                    Interlocked.Decrement(ref picturesWaiting);
                }
            }
        }
        catch (Exception)
        {
            // The connection is gone.
        }

        MarkClosed();
    }

    private static async Task Finish(QuicStream stream)
    {
        try
        {
            await stream.DisposeAsync();
        }
        catch (Exception)
        {
        }
    }

    internal void RaiseMessage(byte[] message)
    {
        if (closed == 0)
        {
            MessageReceived?.Invoke(message);
        }
    }

    internal void MarkClosed()
    {
        if (Interlocked.Exchange(ref closed, 1) == 0)
        {
            outgoing.Writer.TryComplete();
            Closed?.Invoke();
        }
    }
}
