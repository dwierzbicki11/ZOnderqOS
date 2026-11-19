using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;

namespace ZonderqOS.SystemCore.Networking
{
    /// <summary>
    /// Pure managed TLS 1.2 / 1.3 client built on BouncyCastle.Cryptography 2.7.0.
    /// Operates directly over any System.IO.Stream (such as Cosmos plugged TcpClient.GetStream())
    /// without platform OpenSSL/SChannel dependencies.
    /// </summary>
    public sealed class ManagedTlsConnection : IDisposable
    {
        private readonly TlsClientProtocol protocol;
        private bool disposed;

        public Stream Stream => protocol.Stream;

        private ManagedTlsConnection(TlsClientProtocol protocol)
        {
            this.protocol = protocol;
        }

        public static ManagedTlsConnection Connect(Stream transportStream, string serverHost,
            X509Certificate extraTrustedRoot = null)
        {
            if (transportStream == null)
                throw new ArgumentNullException(nameof(transportStream));
            if (string.IsNullOrWhiteSpace(serverHost))
                throw new ArgumentException("Server host is required for TLS verification.", nameof(serverHost));

            SecureRandom random = CreateSecureRandom();
            var crypto = new BcTlsCrypto(random);
            var client = new ZonderqTlsClient(crypto, serverHost.Trim(), extraTrustedRoot);
            var protocol = new TlsClientProtocol(transportStream);
            try
            {
                protocol.Connect(client);
                return new ManagedTlsConnection(protocol);
            }
            catch
            {
                try { protocol.Close(); } catch { }
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try { protocol.Close(); } catch { }
        }

        private static SecureRandom CreateSecureRandom()
        {
            var gen = new DigestRandomGenerator(new Sha256Digest());
            byte[] seed = new byte[64];
            try
            {
                RandomNumberGenerator.Fill(seed);
                gen.AddSeedMaterial(seed);
            }
            catch { }

            gen.AddSeedMaterial(Stopwatch.GetTimestamp());
            gen.AddSeedMaterial(DateTime.UtcNow.Ticks);
            gen.AddSeedMaterial(Guid.NewGuid().ToByteArray());
            gen.AddSeedMaterial(Guid.NewGuid().ToByteArray());
            Array.Clear(seed, 0, seed.Length);
            return new SecureRandom(gen);
        }

        private sealed class ZonderqTlsClient : DefaultTlsClient
        {
            private readonly string serverHost;
            private readonly X509Certificate extraTrustedRoot;

            public ZonderqTlsClient(BcTlsCrypto crypto, string serverHost, X509Certificate extraTrustedRoot)
                : base(crypto)
            {
                this.serverHost = serverHost;
                this.extraTrustedRoot = extraTrustedRoot;
            }

            protected override ProtocolVersion[] GetSupportedVersions()
            {
                return ProtocolVersion.TLSv13.DownTo(ProtocolVersion.TLSv12);
            }

            protected override IList<ServerName> GetSniServerNames()
            {
                // RFC 6066: IPv4 address literals must not be placed in SNI host_name.
                if (OtaUrlParser.IsIPv4Literal(serverHost))
                    return null;

                return new List<ServerName>(1)
                {
                    new ServerName(NameType.host_name, Encoding.ASCII.GetBytes(serverHost))
                };
            }

            public override TlsAuthentication GetAuthentication()
            {
                return new ZonderqTlsAuthentication(serverHost, extraTrustedRoot);
            }
        }

        private sealed class ZonderqTlsAuthentication : ServerOnlyTlsAuthentication
        {
            private readonly string serverHost;
            private readonly X509Certificate extraTrustedRoot;

            public ZonderqTlsAuthentication(string serverHost, X509Certificate extraTrustedRoot)
            {
                this.serverHost = serverHost;
                this.extraTrustedRoot = extraTrustedRoot;
            }

            public override void NotifyServerCertificate(TlsServerCertificate serverCertificate)
            {
                if (serverCertificate == null || serverCertificate.Certificate == null)
                    throw new TlsFatalAlert(AlertDescription.bad_certificate, "Missing server certificate.");

                var rawList = serverCertificate.Certificate.GetCertificateList();
                if (rawList == null || rawList.Length == 0)
                    throw new TlsFatalAlert(AlertDescription.bad_certificate, "Empty server certificate chain.");

                var parser = new X509CertificateParser();
                var chain = new List<X509Certificate>(rawList.Length);
                for (int i = 0; i < rawList.Length; i++)
                {
                    if (rawList[i] == null)
                        continue;
                    X509Certificate cert = parser.ReadCertificate(rawList[i].GetEncoded());
                    if (cert != null)
                        chain.Add(cert);
                }

                if (!TlsTrustStore.VerifyChain(chain, serverHost, extraTrustedRoot, out string error))
                    throw new TlsFatalAlert(AlertDescription.bad_certificate, error);
            }
        }
    }
}
