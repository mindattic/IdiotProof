# FutureState - Cross-Platform Integration Layer

This folder contains abstractions, contracts, and implementations designed to make IdiotProof.Core compatible with multiple front-end platforms and deployment scenarios.

## Supported Platforms (Target)
- **MAUI** - Mobile/Desktop apps (iOS, Android, Windows, macOS)
- **Blazor** - Server-side and WebAssembly
- **React/Angular/Vue** - Modern SPA frameworks via REST/gRPC-Web
- **WPF** - Windows desktop
- **Flutter** - Cross-platform mobile/web
- **Console** - CLI applications

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         CLIENT APPLICATIONS                          │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐        │
│  │  MAUI   │ │ Blazor  │ │  React  │ │   WPF   │ │ Flutter │        │
│  └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘        │
└───────┼───────────┼───────────┼───────────┼───────────┼─────────────┘
        │           │           │           │           │
        ▼           ▼           ▼           ▼           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    COMMUNICATION LAYER                               │
│  ┌──────────────────────┐  ┌──────────────────────┐                 │
│  │   gRPC (Protobuf)    │  │   REST API (JSON)    │                 │
│  │   - High performance  │  │   - Universal        │                 │
│  │   - Type-safe        │  │   - Browser compat   │                 │
│  │   - Streaming        │  │   - Simple debug     │                 │
│  └──────────┬───────────┘  └──────────┬───────────┘                 │
└─────────────┼──────────────────────────┼────────────────────────────┘
              │                          │
              ▼                          ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      SECURITY LAYER                                  │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐    │
│  │    TLS      │ │ Input       │ │    Auth     │ │   Rate      │    │
│  │ Encryption  │ │ Validation  │ │   Tokens    │ │  Limiting   │    │
│  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    ABSTRACTION LAYER                                 │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │              Platform-Agnostic Service Interfaces            │    │
│  │   ITradingService  │  IMarketDataService  │  IStrategyService│    │
│  └─────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
              │
              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      IdiotProof.Core                                 │
│        (Business Logic - Platform Independent)                       │
└─────────────────────────────────────────────────────────────────────┘
```

## Folder Structure

```
FutureState/
├── Protos/                 # Protocol Buffer definitions
│   ├── trading.proto       # Trading messages
│   ├── market_data.proto   # Market data streaming
│   └── strategy.proto      # Strategy definitions
├── Security/               # Security implementations
│   ├── TlsConfiguration.cs # TLS/SSL setup
│   ├── InputSanitizer.cs   # Injection attack prevention
│   ├── TokenService.cs     # JWT/API key auth
│   └── RateLimiter.cs      # DDoS protection
├── Abstractions/           # Platform-agnostic interfaces
│   ├── ITransportLayer.cs  # Communication abstraction
│   ├── IPlatformServices.cs# Platform-specific hooks
│   └── ISecurityProvider.cs# Security abstraction
├── Contracts/              # API contracts
│   ├── RestApiContracts.cs # REST endpoint definitions
│   └── GrpcServices.cs     # gRPC service implementations
└── CrossPlatform/          # Platform compatibility
    ├── PlatformDetector.cs # Runtime platform detection
    └── FeatureFlags.cs     # Platform-specific features
```

## Usage

### Recommended: gRPC with Protobuf
- Use for: MAUI, Blazor Server, WPF, Flutter (via Dart gRPC)
- Benefits: Type-safe, high performance, bi-directional streaming

### Fallback: REST API
- Use for: React, Angular, Vue, Blazor WASM, older browsers
- Benefits: Universal compatibility, easy debugging

## Security Checklist
- [ ] TLS 1.3 for all communications
- [ ] Input sanitization on all user inputs
- [ ] SQL injection prevention (parameterized queries)
- [ ] XSS prevention (output encoding)
- [ ] JWT token validation
- [ ] Rate limiting per client
- [ ] Request signing for sensitive operations
