// IdiotProof.Core.FutureState.Security
// TLS/SSL Configuration for secure communications
// Supports both gRPC and REST endpoints

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IdiotProof.Core.FutureState.Security;

/// <summary>
/// TLS configuration options for secure communications.
/// Supports TLS 1.2 and 1.3 (1.3 preferred).
/// </summary>
public class TlsConfiguration
{
    /// <summary>
    /// Minimum allowed TLS version. Default is TLS 1.2.
    /// </summary>
    public TlsVersion MinimumVersion { get; set; } = TlsVersion.Tls12;
    
    /// <summary>
    /// Path to server certificate (.pfx or .p12).
    /// </summary>
    public string? CertificatePath { get; set; }
    
    /// <summary>
    /// Password for the certificate (should come from secure storage).
    /// </summary>
    public string? CertificatePassword { get; set; }
    
    /// <summary>
    /// Whether to require client certificates (mTLS).
    /// Recommended for service-to-service communication.
    /// </summary>
    public bool RequireClientCertificate { get; set; } = false;
    
    /// <summary>
    /// Path to trusted CA certificates for client verification.
    /// </summary>
    public string? TrustedCaPath { get; set; }
    
    /// <summary>
    /// Cipher suites to allow. Empty = use defaults.
    /// </summary>
    public string[] AllowedCipherSuites { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Certificate revocation check mode.
    /// </summary>
    public CertificateRevocationMode RevocationMode { get; set; } = CertificateRevocationMode.Online;
    
    /// <summary>
    /// Whether to allow self-signed certificates (DEV ONLY).
    /// </summary>
    public bool AllowSelfSignedCertificates { get; set; } = false;
}

public enum TlsVersion
{
    Tls12 = 12,
    Tls13 = 13
}

public enum CertificateRevocationMode
{
    /// <summary>No revocation check (not recommended).</summary>
    None,
    /// <summary>Check online CRL/OCSP.</summary>
    Online,
    /// <summary>Use cached CRL only.</summary>
    Offline
}

/// <summary>
/// Manages TLS certificates and connections.
/// </summary>
public interface ITlsProvider
{
    /// <summary>
    /// Loads and validates the server certificate.
    /// </summary>
    X509Certificate2 LoadServerCertificate();
    
    /// <summary>
    /// Validates a client certificate.
    /// </summary>
    bool ValidateClientCertificate(X509Certificate2 certificate, out string errorMessage);
    
    /// <summary>
    /// Gets the TLS configuration for gRPC channels.
    /// </summary>
    object GetGrpcChannelCredentials();
    
    /// <summary>
    /// Gets the TLS configuration for HttpClient.
    /// </summary>
    object GetHttpClientHandler();
}

/// <summary>
/// Default implementation of ITlsProvider.
/// </summary>
public class TlsProvider : ITlsProvider
{
    private readonly TlsConfiguration config;
    private X509Certificate2? serverCertificate;
    
    public TlsProvider(TlsConfiguration config)
    {
        config = config ?? throw new ArgumentNullException(nameof(config));
    }
    
    public X509Certificate2 LoadServerCertificate()
    {
        if (serverCertificate != null)
            return serverCertificate;
            
        if (string.IsNullOrEmpty(config.CertificatePath))
        {
            throw new InvalidOperationException(
                "Certificate path not configured. Set CertificatePath in TlsConfiguration.");
        }
        
        if (!File.Exists(config.CertificatePath))
        {
            throw new FileNotFoundException(
                $"Certificate file not found: {config.CertificatePath}");
        }
        
        serverCertificate = new X509Certificate2(
            config.CertificatePath,
            config.CertificatePassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
            
        // Validate certificate
        if (serverCertificate.NotAfter < DateTime.UtcNow)
        {
            throw new InvalidOperationException(
                $"Server certificate expired on {serverCertificate.NotAfter}");
        }
        
        if (serverCertificate.NotBefore > DateTime.UtcNow)
        {
            throw new InvalidOperationException(
                $"Server certificate not valid until {serverCertificate.NotBefore}");
        }
        
        return serverCertificate;
    }
    
    public bool ValidateClientCertificate(X509Certificate2 certificate, out string errorMessage)
    {
        errorMessage = string.Empty;
        
        if (certificate == null)
        {
            errorMessage = "No client certificate provided";
            return false;
        }
        
        // Check expiration
        if (certificate.NotAfter < DateTime.UtcNow)
        {
            errorMessage = $"Client certificate expired on {certificate.NotAfter}";
            return false;
        }
        
        if (certificate.NotBefore > DateTime.UtcNow)
        {
            errorMessage = $"Client certificate not valid until {certificate.NotBefore}";
            return false;
        }
        
        // Build certificate chain
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = config.RevocationMode switch
        {
            CertificateRevocationMode.None => X509RevocationMode.NoCheck,
            CertificateRevocationMode.Online => X509RevocationMode.Online,
            CertificateRevocationMode.Offline => X509RevocationMode.Offline,
            _ => X509RevocationMode.Online
        };
        
        // Add trusted CA if configured
        if (!string.IsNullOrEmpty(config.TrustedCaPath) && File.Exists(config.TrustedCaPath))
        {
            var trustedCa = new X509Certificate2(config.TrustedCaPath);
            chain.ChainPolicy.ExtraStore.Add(trustedCa);
        }
        
        if (!chain.Build(certificate))
        {
            errorMessage = $"Certificate chain validation failed: {chain.ChainStatus.Length} issues";
            return false;
        }
        
        return true;
    }
    
    public object GetGrpcChannelCredentials()
    {
        // This would return Grpc.Core.SslCredentials or similar
        // Implementation depends on gRPC package version
        throw new NotImplementedException(
            "Implement with specific gRPC package (Grpc.Net.Client or Grpc.Core)");
    }
    
    public object GetHttpClientHandler()
    {
        // Returns configured HttpClientHandler for REST clients
        throw new NotImplementedException(
            "Implement with specific HTTP client requirements");
    }
}

/// <summary>
/// Factory for creating development certificates (self-signed).
/// USE ONLY FOR DEVELOPMENT/TESTING.
/// </summary>
public static class DevelopmentCertificateFactory
{
    /// <summary>
    /// Creates a self-signed certificate for development.
    /// NEVER use in production.
    /// </summary>
    public static X509Certificate2 CreateDevelopmentCertificate(
        string subjectName = "CN=localhost",
        int validityDays = 365)
    {
        using var rsa = RSA.Create(4096);
        
        var request = new CertificateRequest(
            subjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
            
        // Add extensions
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
            
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
                
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        
        // SAN for localhost
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());
        
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(validityDays));
            
        // Export and reimport to get exportable private key
        var pfxBytes = certificate.Export(X509ContentType.Pfx, "dev-password");
        return new X509Certificate2(pfxBytes, "dev-password", 
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    }
}
