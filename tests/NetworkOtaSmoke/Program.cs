using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using BcX509 = Org.BouncyCastle.X509;
using ZonderqOS.SystemCore.Networking;
using ZonderqOS.SystemCore.Updates;

namespace NetworkOtaSmoke
{
    internal static class Program
    {
        private static void Assert(bool cond, string message)
        {
            if (!cond)
                throw new Exception("ASSERTION FAILED: " + message);
        }

        private static int Main()
        {
            Console.WriteLine("[NET-OTA-TEST] Starting ZonderqOS TLS 1.2/1.3 + HTTPS OTA Smoke Suite...");

            TestUrlParser();
            TestHostnameMatcher();
            TestEmbeddedTrustStore();
            TestRealTlsHttpAndEd25519OtaEndToEnd();
            TestLiveGitHubTlsHandshakeIfOnline();

            Console.WriteLine("[NET-OTA-TEST] ALL NETWORK & HTTPS OTA TESTS PASSED [PASS]");
            return 0;
        }

        private static void TestUrlParser()
        {
            Console.WriteLine("  -> Testing OtaUrlParser & redirect downgrade protection...");
            Assert(OtaUrlParser.TryParse("https://github.com/dwierzbicki11/ZOnderqOS/releases/latest/download/update.manifest",
                false, out ParsedOtaUrl u, out _), "Valid GitHub HTTPS URL failed");
            Assert(u.IsHttps && u.Host == "github.com" && u.Port == 443 &&
                   u.PathAndQuery == "/dwierzbicki11/ZOnderqOS/releases/latest/download/update.manifest", "Parsed fields mismatch");

            Assert(!OtaUrlParser.TryParse("http://example.com/update.manifest", false, out _, out _),
                "Plaintext HTTP must fail when allowInsecureHttp=false");
            Assert(OtaUrlParser.TryParse("http://10.0.2.2:18080/update.manifest", true, out ParsedOtaUrl uHttp, out _),
                "Explicitly allowed HTTP should succeed");
            Assert(!uHttp.IsHttps && uHttp.Port == 18080 && uHttp.HostHeader == "10.0.2.2:18080", "HTTP host/port mismatch");

            Assert(!OtaUrlParser.TryParse("https://github.com/a\r\nEvil: 1", false, out _, out _),
                "CRLF injection must be rejected");
            Assert(!OtaUrlParser.TryParse("https://user@github.com/file", false, out _, out _),
                "Userinfo '@' must be rejected");

            Assert(OtaUrlParser.TryResolveRedirect(u, "https://objects.githubusercontent.com/asset?sig=1", false,
                out ParsedOtaUrl r1, out _), "HTTPS -> HTTPS redirect failed");
            Assert(r1.Host == "objects.githubusercontent.com" && r1.PathAndQuery == "/asset?sig=1", "Redirect target mismatch");

            Assert(!OtaUrlParser.TryResolveRedirect(u, "http://objects.githubusercontent.com/asset", true,
                out _, out _), "HTTPS -> HTTP downgrade must ALWAYS be rejected");

            Assert(OtaUrlParser.TryResolveRedirect(u, "/v2/update.manifest", false, out ParsedOtaUrl r2, out _),
                "Root-relative redirect failed");
            Assert(r2.Host == "github.com" && r2.PathAndQuery == "/v2/update.manifest", "Root-relative mismatch");
        }

        private static void TestHostnameMatcher()
        {
            Console.WriteLine("  -> Testing RFC 6125 wildcard & hostname matching...");
            Assert(TlsHostnameMatcher.MatchDnsPattern("github.com", "GITHUB.COM"), "Case-insensitive exact match failed");
            Assert(TlsHostnameMatcher.MatchDnsPattern("*.github.io", "objects.github.io"), "Wildcard single label failed");
            Assert(!TlsHostnameMatcher.MatchDnsPattern("*.github.io", "a.b.github.io"), "Wildcard must not span two labels");
            Assert(!TlsHostnameMatcher.MatchDnsPattern("*.github.io", "github.io"), "Wildcard must not match apex");
            Assert(!TlsHostnameMatcher.MatchDnsPattern("*.com", "github.com"), "Single-TLD wildcard must be rejected");
            Assert(!TlsHostnameMatcher.MatchDnsPattern("g*.github.com", "git.github.com"), "Partial wildcard must be rejected");
        }

        private static void TestEmbeddedTrustStore()
        {
            Console.WriteLine("  -> Testing embedded Root CA trust store...");
            var roots = TlsTrustStore.GetBuiltInRoots();
            Assert(roots != null && roots.Length >= 12, "Expected at least 12 embedded Root CAs, got " + (roots?.Length ?? 0));
            Console.WriteLine("     Loaded " + roots.Length + " embedded Root CAs (GitHub/Let's Encrypt/DigiCert/Sectigo/USERTrust/GlobalSign/GTS/Amazon/Microsoft).");
        }

        private static void TestRealTlsHttpAndEd25519OtaEndToEnd()
        {
            Console.WriteLine("  -> Testing real TLS 1.2/1.3 server, 302 redirect, chunked encoding, and Ed25519 OTA download...");

            // 1. Create synthetic Root CA + leaf certificate for ota.zonderqos.local + 127.0.0.1
            using RSA caKey = RSA.Create(2048);
            var caReq = new CertificateRequest("CN=ZonderqOS Test Root CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using X509Certificate2 rootCert = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            using RSA srvKey = RSA.Create(2048);
            var srvReq = new CertificateRequest("CN=ota.zonderqos.local", srvKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            srvReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("ota.zonderqos.local");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            srvReq.CertificateExtensions.Add(sanBuilder.Build());
            byte[] serial = new byte[8];
            RandomNumberGenerator.Fill(serial);
            using X509Certificate2 signedLeaf = srvReq.Create(rootCert, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(10), serial);
            using X509Certificate2 serverCertWithKey = signedLeaf.CopyWithPrivateKey(srvKey);

            BcX509.X509Certificate bcRoot = new BcX509.X509CertificateParser().ReadCertificate(rootCert.RawData);

            // 2. Create real Ed25519 release keypair and a valid synthetic ELF64 + payload file
            var kpGen = new Ed25519KeyPairGenerator();
            kpGen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var kp = kpGen.GenerateKeyPair();
            var privKey = (Ed25519PrivateKeyParameters)kp.Private;
            var pubKeyBytes = ((Ed25519PublicKeyParameters)kp.Public).GetEncoded();

            byte[] validElf = BuildMinimalValidX64Elf(65536);
            string elfSha = ComputeSha256Hex(validElf);
            byte[] payloadFile = Encoding.UTF8.GetBytes("ZonderqOS OTA verified payload file v9.9.9\n");
            string payloadSha = ComputeSha256Hex(payloadFile);

            string manifestText =
                "UPDATE_FORMAT=2\n" +
                "SIGNATURE_ALG=ED25519\n" +
                "VERSION=9.9.9\n" +
                "BUILD=9999\n" +
                "ARCH=x86_64\n" +
                "TITLE=ZonderqOS OTA TLS Test Release\n" +
                "DATE=2026-11-19\n" +
                "SEVERITY=Recommended\n" +
                "KERNEL_IMAGE=ZonderqOS.elf\n" +
                "KERNEL_SHA256=" + elfSha + "\n" +
                "PAYLOAD_DIR=payload\n" +
                "FILE=usr/share/ota-test.txt:" + payloadSha + ":" + payloadFile.Length + "\n" +
                "CHANGELOG=Real HTTPS TLS 1.2/1.3 OTA;Streaming SHA-256 verification\n" +
                "COMPONENTS=Kernel ELF;Managed TLS\n";

            byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestText);
            var signer = new Ed25519Signer();
            signer.Init(true, privKey);
            signer.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
            byte[] validSig = signer.GenerateSignature();

            var assets = new Dictionary<string, (byte[] body, bool chunked, string redirect)>(StringComparer.Ordinal)
            {
                ["/latest/update.manifest"] = (null, false, "/releases/v9.9.9/update.manifest"),
                ["/releases/v9.9.9/update.manifest"] = (manifestBytes, true, null), // chunked!
                ["/latest/update.manifest.sig"] = (validSig, false, null),
                ["/latest/ZonderqOS.elf"] = (validElf, false, null),
                // Simulate GitHub Releases flat asset fallback (404 on nested, 200 on payload__...)
                ["/latest/payload__usr_share_ota-test.txt"] = (payloadFile, false, null),
                ["/tampered/update.manifest"] = (manifestBytes, false, null),
                ["/tampered/update.manifest.sig"] = (new byte[64], false, null),
            };

            // 3. Launch real local TLS 1.2/1.3 listener
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource();

            Task serverTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(cts.Token); }
                    catch { break; }

                    _ = Task.Run(async () =>
                    {
                        using (client)
                        using (var ssl = new SslStream(client.GetStream(), false))
                        {
                            try
                            {
                                await ssl.AuthenticateAsServerAsync(serverCertWithKey, false,
                                    SslProtocols.Tls12 | SslProtocols.Tls13, false);

                                byte[] reqBuf = new byte[4096];
                                int n = await ssl.ReadAsync(reqBuf, 0, reqBuf.Length);
                                if (n <= 0) return;
                                string req = Encoding.ASCII.GetString(reqBuf, 0, n);
                                string firstLine = req.Split('\n')[0].Trim();
                                string[] parts = firstLine.Split(' ');
                                string path = parts.Length >= 2 ? parts[1] : "/";

                                if (!assets.TryGetValue(path, out var entry))
                                {
                                    byte[] resp404 = Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                                    await ssl.WriteAsync(resp404, 0, resp404.Length);
                                    await ssl.FlushAsync();
                                    return;
                                }

                                if (entry.redirect != null)
                                {
                                    string r = "HTTP/1.1 302 Found\r\nLocation: " + entry.redirect + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                                    byte[] rb = Encoding.ASCII.GetBytes(r);
                                    await ssl.WriteAsync(rb, 0, rb.Length);
                                    await ssl.FlushAsync();
                                    return;
                                }

                                if (entry.chunked)
                                {
                                    string hdr = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n";
                                    byte[] hb = Encoding.ASCII.GetBytes(hdr);
                                    await ssl.WriteAsync(hb, 0, hb.Length);
                                    int mid = entry.body.Length / 2;
                                    await WriteChunkAsync(ssl, entry.body, 0, mid);
                                    await WriteChunkAsync(ssl, entry.body, mid, entry.body.Length - mid);
                                    byte[] endChunk = Encoding.ASCII.GetBytes("0\r\n\r\n");
                                    await ssl.WriteAsync(endChunk, 0, endChunk.Length);
                                    await ssl.FlushAsync();
                                }
                                else
                                {
                                    string hdr = "HTTP/1.1 200 OK\r\nContent-Length: " + entry.body.Length + "\r\nConnection: close\r\n\r\n";
                                    byte[] hb = Encoding.ASCII.GetBytes(hdr);
                                    await ssl.WriteAsync(hb, 0, hb.Length);
                                    await ssl.WriteAsync(entry.body, 0, entry.body.Length);
                                    await ssl.FlushAsync();
                                }
                            }
                            catch { }
                        }
                    });
                }
            });

            string tempDir = Path.Combine(Path.GetTempPath(), "zonderq_ota_smoke_" + Guid.NewGuid().ToString("N"));
            try
            {
                string baseUrl = "https://ota.zonderqos.local:" + port + "/latest";
                Func<string, string> localDns = h => "127.0.0.1";
                Func<byte[], byte[], bool> edVerifier = (msg, sig) => ReleaseSignatureVerifier.VerifyEd25519(pubKeyBytes, msg, sig);

                // A. Untrusted CA MUST fail closed
                var untrustedOpts = new HttpDownloadOptions { DnsResolver = localDns, ExtraTrustedRoot = null };
                Assert(!OtaUpdateClient.CheckRemoteRelease(baseUrl, "1.0.2", "3120", false, untrustedOpts, edVerifier, out var badCa),
                    "Untrusted CA must be rejected");
                Console.WriteLine("     [OK] Untrusted CA rejected: " + badCa.Error);

                // B. Wrong hostname MUST fail closed even with trusted CA
                var wrongHostOpts = new HttpDownloadOptions { DnsResolver = localDns, ExtraTrustedRoot = bcRoot };
                Assert(!OtaUpdateClient.CheckRemoteRelease("https://wrong.zonderqos.local:" + port + "/latest",
                    "1.0.2", "3120", false, wrongHostOpts, edVerifier, out var badHost),
                    "Wrong TLS hostname must be rejected");
                Console.WriteLine("     [OK] Hostname mismatch rejected: " + badHost.Error);

                // C. Invalid Ed25519 signature MUST fail closed before downloading kernel
                var trustedOpts = new HttpDownloadOptions { DnsResolver = localDns, ExtraTrustedRoot = bcRoot };
                Assert(!OtaUpdateClient.CheckRemoteRelease("https://ota.zonderqos.local:" + port + "/tampered",
                    "1.0.2", "3120", false, trustedOpts, edVerifier, out var badSig),
                    "Forged Ed25519 signature must be rejected");
                Console.WriteLine("     [OK] Forged Ed25519 signature rejected: " + badSig.Error);

                // D. Full valid HTTPS TLS + 302 redirect + chunked manifest + Ed25519 + streaming ELF + flat payload fallback
                Assert(OtaUpdateClient.CheckRemoteRelease(baseUrl, "1.0.2", "3120", false, trustedOpts, edVerifier, out var goodCheck),
                    "Valid signed OTA check failed: " + goodCheck?.Error);
                Assert(goodCheck.UpdateAvailable && goodCheck.Manifest.Version == "9.9.9", "Expected version 9.9.9 available");

                Assert(OtaUpdateClient.DownloadVerifiedPackageToDirectory(goodCheck, tempDir, trustedOpts, null,
                    out string stagedManifest, out string dlErr),
                    "OTA download failed: " + dlErr);
                Assert(File.Exists(stagedManifest), "Staged manifest missing");
                Assert(File.Exists(Path.Combine(tempDir, "update.manifest.sig")), "Staged sig missing");
                Assert(File.Exists(Path.Combine(tempDir, "ZonderqOS.elf")), "Staged kernel missing");
                Assert(File.Exists(Path.Combine(tempDir, "payload/usr/share/ota-test.txt")), "Staged payload file missing");
                Console.WriteLine("     [OK] Full HTTPS TLS + 302 + chunked + Ed25519 + SHA-256 streaming download verified.");
            }
            finally
            {
                cts.Cancel();
                listener.Stop();
                try { serverTask.Wait(1000); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void TestLiveGitHubTlsHandshakeIfOnline()
        {
            Console.WriteLine("  -> Testing live ManagedTlsConnection against github.com:443 & raw.githubusercontent.com:443 using embedded roots ONLY...");
            foreach (string host in new[] { "github.com", "raw.githubusercontent.com" })
            {
                try
                {
                    IPAddress[] ips = Dns.GetHostAddresses(host);
                    IPAddress ipv4 = null;
                    for (int i = 0; i < ips.Length; i++)
                    {
                        if (ips[i].AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipv4 = ips[i];
                            break;
                        }
                    }
                    if (ipv4 == null)
                    {
                        Console.WriteLine("     [SKIP] Offline (no DNS for " + host + ")");
                        return;
                    }

                    using var tcp = new TcpClient();
                    tcp.Connect(ipv4, 443);
                    tcp.Client.ReceiveTimeout = 10000;
                    tcp.Client.SendTimeout = 10000;
                    using var tls = ManagedTlsConnection.Connect(tcp.GetStream(), host, extraTrustedRoot: null);
                    byte[] req = Encoding.ASCII.GetBytes("HEAD / HTTP/1.1\r\nHost: " + host + "\r\nConnection: close\r\n\r\n");
                    tls.Stream.Write(req, 0, req.Length);
                    tls.Stream.Flush();
                    byte[] resp = new byte[64];
                    int n = tls.Stream.Read(resp, 0, resp.Length);
                    string line = Encoding.ASCII.GetString(resp, 0, Math.Max(0, n)).Split('\n')[0].Trim();
                    Assert(line.StartsWith("HTTP/1.", StringComparison.Ordinal), "Unexpected GitHub HTTP response: " + line);
                    Console.WriteLine("     [OK] Live TLS 1.2/1.3 to " + host + ":443 succeeded with embedded TlsTrustStore -> " + line);
                }
                catch (SocketException)
                {
                    Console.WriteLine("     [SKIP] Host appears offline for " + host);
                    return;
                }
            }
        }

        private static async Task WriteChunkAsync(Stream s, byte[] data, int offset, int count)
        {
            byte[] hdr = Encoding.ASCII.GetBytes(count.ToString("x") + "\r\n");
            await s.WriteAsync(hdr, 0, hdr.Length);
            await s.WriteAsync(data, offset, count);
            byte[] crlf = Encoding.ASCII.GetBytes("\r\n");
            await s.WriteAsync(crlf, 0, crlf.Length);
        }

        private static byte[] BuildMinimalValidX64Elf(int size)
        {
            byte[] img = new byte[Math.Max(size, 64)];
            img[0] = 0x7F; img[1] = (byte)'E'; img[2] = (byte)'L'; img[3] = (byte)'F';
            img[4] = 2; // ELFCLASS64
            img[5] = 1; // ELFDATA2LSB
            img[6] = 1; // EV_CURRENT
            img[16] = 2; // ET_EXEC
            img[18] = 62; // EM_X86_64
            img[20] = 1;
            // e_ehsize = 64
            img[52] = 64;
            // e_phentsize = 56, e_phnum = 1
            img[54] = 56;
            img[56] = 1;
            // e_phoff = 64
            img[32] = 64;
            return img;
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
