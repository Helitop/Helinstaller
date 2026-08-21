// FILE [CS]: .\Helpers\MtprotoProbeHelper.cs

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Helinstaller.Helpers
{
    public static class MtprotoProbeHelper
    {
        private const int DigestPos = 11;
        private static readonly ConcurrentDictionary<string, IPAddress> DnsCache = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<(bool IsOnline, long Ping)> ProbeProxyAsync(
            string host,
            int port,
            string rawSecretStr,
            int timeoutMs,
            CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            string cleanHost = WebUtility.UrlDecode(host ?? "").Trim().TrimEnd('.');
            string proxyTag = $"{cleanHost}:{port}";

            if (string.IsNullOrWhiteSpace(cleanHost) || cleanHost.Contains('%') || cleanHost.Length > 100)
            {
                return (false, -1);
            }

            try
            {
                // 1. Декодирование секрета
                if (!TryParseSecret(rawSecretStr, out byte[] rawSecret, out string sniDomain, out bool isFakeTls))
                {
                    return (false, -1);
                }

                int effectiveTimeout = Math.Max(timeoutMs, 3000);
                using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                // 2. DNS
                IPAddress targetIp;
                if (IPAddress.TryParse(cleanHost, out var parsedIp))
                {
                    targetIp = parsedIp;
                }
                else if (DnsCache.TryGetValue(cleanHost, out var cachedIp))
                {
                    targetIp = cachedIp;
                }
                else
                {
                    using var dnsCts = new CancellationTokenSource(1200);
                    using var dnsLinked = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token, dnsCts.Token);
                    try
                    {
                        var addresses = await Dns.GetHostAddressesAsync(cleanHost, dnsLinked.Token).ConfigureAwait(false);
                        if (addresses.Length == 0) return (false, -1);
                        targetIp = addresses[0];
                        DnsCache.TryAdd(cleanHost, targetIp);
                    }
                    catch
                    {
                        return (false, -1);
                    }
                }

                // 3. TCP Connect
                using var client = new TcpClient();
                await client.ConnectAsync(targetIp, port, linkedCts.Token).ConfigureAwait(false);
                using var stream = client.GetStream();

                // -------------------------------------------------------------
                // 4. ПРОВЕРКА FAKE-TLS
                // -------------------------------------------------------------
                if (isFakeTls)
                {
                    byte[] clientHello = BuildTlsClientHelloTemplate(sniDomain);

                    byte[] clientDigest;
                    using (var hmac = new HMACSHA256(rawSecret))
                    {
                        clientDigest = hmac.ComputeHash(clientHello);
                    }

                    uint unixTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    clientDigest[28] ^= (byte)(unixTime & 0xFF);
                    clientDigest[29] ^= (byte)((unixTime >> 8) & 0xFF);
                    clientDigest[30] ^= (byte)((unixTime >> 16) & 0xFF);
                    clientDigest[31] ^= (byte)((unixTime >> 24) & 0xFF);

                    Array.Copy(clientDigest, 0, clientHello, DigestPos, 32);

                    await stream.WriteAsync(clientHello, linkedCts.Token).ConfigureAwait(false);

                    byte[] responseBuffer = new byte[2048];
                    int totalRead = 0;

                    try
                    {
                        while (totalRead < 43)
                        {
                            int read = await stream.ReadAsync(responseBuffer.AsMemory(totalRead, responseBuffer.Length - totalRead), linkedCts.Token).ConfigureAwait(false);
                            if (read == 0) break;
                            totalRead += read;
                        }
                    }
                    catch { }

                    sw.Stop();

                    // Отсекаем HTTP-ответы и TLS Alerts
                    if (IsHttpOrAlertResponse(responseBuffer, totalRead))
                    {
                        return (false, -1);
                    }

                    if (totalRead >= 43 && responseBuffer[0] == 0x16)
                    {
                        byte[] serverDigest = new byte[32];
                        Array.Copy(responseBuffer, DigestPos, serverDigest, 0, 32);

                        byte[] responseZeroed = new byte[totalRead];
                        Array.Copy(responseBuffer, 0, responseZeroed, 0, totalRead);
                        Array.Clear(responseZeroed, DigestPos, 32);

                        byte[] toHash = new byte[32 + totalRead];
                        Array.Copy(clientDigest, 0, toHash, 0, 32);
                        Array.Copy(responseZeroed, 0, toHash, 32, totalRead);

                        byte[] expectedDigest;
                        using (var hmac = new HMACSHA256(rawSecret))
                        {
                            expectedDigest = hmac.ComputeHash(toHash);
                        }

                        // Только при идеальном совпадении крипто-подписи сервера
                        if (CryptographicOperations.FixedTimeEquals(serverDigest, expectedDigest))
                        {
                            Logger.LogInfo($"[PROXY CHECK] [{proxyTag}] === УСПЕХ: 100% рабочий Fake-TLS! Пинг: {sw.ElapsedMilliseconds} мс ===");
                            return (true, sw.ElapsedMilliseconds);
                        }
                    }

                    return (false, -1);
                }
                // -------------------------------------------------------------
                // 5. ПРОВЕРКА КЛАССИЧЕСКИХ / DD СЕКРЕТОВ
                // -------------------------------------------------------------
                else
                {
                    byte[] obfuscatedHeader = BuildObfuscated2Header(rawSecret);
                    await stream.WriteAsync(obfuscatedHeader, linkedCts.Token).ConfigureAwait(false);

                    byte[] resp = new byte[256];
                    using var readTimeout = new CancellationTokenSource(1500);
                    using var readLinked = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token, readTimeout.Token);

                    try
                    {
                        int read = await stream.ReadAsync(resp, readLinked.Token).ConfigureAwait(false);
                        sw.Stop();

                        // Если ответил веб-сервер (HTTP 400/200) -> это не прокси!
                        if (IsHttpOrAlertResponse(resp, read))
                        {
                            return (false, -1);
                        }

                        // В настоящем классическом MTProto ответ никогда не начинается с текстовых символов HTTP
                        if (read >= 4 && resp[0] != 'H' && resp[0] != '<')
                        {
                            Logger.LogInfo($"[PROXY CHECK] [{proxyTag}] === УСПЕХ (Classic MTProto) ===");
                            return (true, sw.ElapsedMilliseconds);
                        }
                    }
                    catch { }

                    return (false, -1);
                }
            }
            catch
            {
                return (false, -1);
            }
        }

        private static bool IsHttpOrAlertResponse(byte[] buffer, int len)
        {
            if (len < 4) return false;

            // TLS Alert (0x15 0x03 ...)
            if (buffer[0] == 0x15 && buffer[1] == 0x03) return true;

            // HTTP 1.X / HTML ответы веб-сайтов (HTTP, POST, GET, HEAD, <htm)
            if (buffer[0] == 'H' && buffer[1] == 'T' && buffer[2] == 'T' && buffer[3] == 'P') return true;
            if (buffer[0] == '<' && (buffer[1] == 'h' || buffer[1] == 'H' || buffer[1] == '!')) return true;

            return false;
        }

        private static bool TryParseSecret(string secretStr, out byte[] rawSecret, out string sniDomain, out bool isFakeTls)
        {
            rawSecret = Array.Empty<byte>();
            sniDomain = "www.google.com";
            isFakeTls = false;

            if (string.IsNullOrWhiteSpace(secretStr)) return false;

            string clean = WebUtility.UrlDecode(secretStr).Trim();

            byte[] secretBytes;

            if (Regex.IsMatch(clean, @"\A\b[0-9a-fA-F]+\b\Z") && clean.Length % 2 == 0)
            {
                secretBytes = Convert.FromHexString(clean);
            }
            else
            {
                try
                {
                    string b64 = clean.Replace('-', '+').Replace('_', '/');
                    switch (b64.Length % 4)
                    {
                        case 2: b64 += "=="; break;
                        case 3: b64 += "="; break;
                    }
                    secretBytes = Convert.FromBase64String(b64);
                }
                catch
                {
                    return false;
                }
            }

            if (secretBytes.Length < 16) return false;

            if (secretBytes[0] == 0xee && secretBytes.Length >= 17)
            {
                isFakeTls = true;
                rawSecret = new byte[16];
                Array.Copy(secretBytes, 1, rawSecret, 0, 16);

                if (secretBytes.Length > 17)
                {
                    string domain = Encoding.ASCII.GetString(secretBytes, 17, secretBytes.Length - 17).Trim().TrimStart('-').TrimEnd('.');
                    if (!string.IsNullOrEmpty(domain) && domain.Contains('.'))
                    {
                        sniDomain = domain;
                    }
                }
                return true;
            }

            if (secretBytes.Length > 17 && secretBytes[0] != 0xdd)
            {
                isFakeTls = true;
                rawSecret = new byte[16];
                Array.Copy(secretBytes, 0, rawSecret, 0, 16);

                string domain = Encoding.ASCII.GetString(secretBytes, 16, secretBytes.Length - 16).Trim().TrimStart('-').TrimEnd('.');
                if (!string.IsNullOrEmpty(domain) && domain.Contains('.'))
                {
                    sniDomain = domain;
                }
                return true;
            }

            if (secretBytes[0] == 0xdd && secretBytes.Length >= 17)
            {
                rawSecret = new byte[16];
                Array.Copy(secretBytes, 1, rawSecret, 0, 16);
                return true;
            }

            if (secretBytes.Length == 16)
            {
                rawSecret = secretBytes;
                return true;
            }

            return false;
        }

        private static byte[] BuildTlsClientHelloTemplate(string sniHost)
        {
            using var payload = new MemoryStream();
            using var w = new BinaryWriter(payload);

            w.Write((byte)0x03);
            w.Write((byte)0x03);

            w.Write(new byte[32]);

            w.Write((byte)32);
            byte[] sessionId = new byte[32];
            RandomNumberGenerator.Fill(sessionId);
            w.Write(sessionId);

            byte[] ciphers = {
                0x00, 0x10,
                0x13, 0x01,
                0x13, 0x02,
                0x13, 0x03,
                0xc0, 0x2b,
                0xc0, 0x2f,
                0xc0, 0x2c,
                0xc0, 0x30,
                0xcc, 0xa9
            };
            w.Write(ciphers);

            w.Write((byte)1);
            w.Write((byte)0);

            using var extMs = new MemoryStream();
            using var extW = new BinaryWriter(extMs);

            byte[] sniBytes = Encoding.ASCII.GetBytes(sniHost);
            extW.Write((byte)0x00); extW.Write((byte)0x00);
            extW.Write((byte)0x00); extW.Write((byte)(sniBytes.Length + 5));
            extW.Write((byte)0x00); extW.Write((byte)(sniBytes.Length + 3));
            extW.Write((byte)0x00);
            extW.Write((byte)0x00); extW.Write((byte)sniBytes.Length);
            extW.Write(sniBytes);

            extW.Write((byte)0x00); extW.Write((byte)0x2b);
            extW.Write((byte)0x00); extW.Write((byte)0x03);
            extW.Write((byte)0x02);
            extW.Write((byte)0x03); extW.Write((byte)0x04);

            extW.Write((byte)0x00); extW.Write((byte)0x0a);
            extW.Write((byte)0x00); extW.Write((byte)0x06);
            extW.Write((byte)0x00); extW.Write((byte)0x04);
            extW.Write((byte)0x00); extW.Write((byte)0x1d);
            extW.Write((byte)0x00); extW.Write((byte)0x17);

            extW.Write((byte)0x00); extW.Write((byte)0x33);
            extW.Write((byte)0x00); extW.Write((byte)0x26);
            extW.Write((byte)0x00); extW.Write((byte)0x24);
            extW.Write((byte)0x00); extW.Write((byte)0x1d);
            extW.Write((byte)0x00); extW.Write((byte)0x20);
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);
            extW.Write(key);

            byte[] allExt = extMs.ToArray();
            w.Write((byte)(allExt.Length >> 8));
            w.Write((byte)(allExt.Length & 0xFF));
            w.Write(allExt);

            byte[] clientHelloBody = payload.ToArray();

            using var hsMs = new MemoryStream();
            using var hw = new BinaryWriter(hsMs);
            hw.Write((byte)0x01);
            hw.Write((byte)0x00);
            hw.Write((byte)(clientHelloBody.Length >> 8));
            hw.Write((byte)(clientHelloBody.Length & 0xFF));
            hw.Write(clientHelloBody);

            byte[] handshakeData = hsMs.ToArray();

            using var recMs = new MemoryStream();
            using var rw = new BinaryWriter(recMs);
            rw.Write((byte)0x16);
            rw.Write((byte)0x03);
            rw.Write((byte)0x01);
            rw.Write((byte)(handshakeData.Length >> 8));
            rw.Write((byte)(handshakeData.Length & 0xFF));
            rw.Write(handshakeData);

            return recMs.ToArray();
        }

        private static byte[] BuildObfuscated2Header(byte[] secret)
        {
            byte[] buffer = new byte[64];
            while (true)
            {
                RandomNumberGenerator.Fill(buffer);

                if (buffer[0] == 0xef) continue;

                uint first4 = BitConverter.ToUInt32(buffer, 0);
                if (first4 == 0x44414548 || first4 == 0x54534f50 || first4 == 0x20544547 || first4 == 0x4954504f)
                    continue;

                buffer[56] = 0xdd;
                buffer[57] = 0xdd;
                buffer[58] = 0xdd;
                buffer[59] = 0xdd;
                break;
            }
            return buffer;
        }
    }
}