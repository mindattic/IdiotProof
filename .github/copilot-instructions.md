# Copilot Instructions

## Project Guidelines
- User intends IdiotScript chained conditions to be evaluated sequentially (state machine over time), not as a single simultaneous AND condition.
- In IdiotProof, the backend doesn't load strategies directly; it reads strategies retrieved from the frontend, which gets them from .idiot files. The data flow is: .idiot files → Frontend → Backend.
- IdiotScript commands should always include parentheses, even for flag-style commands without parameters (e.g., `AboveVwap()` not `AboveVwap`, `Breakout()` not `Breakout`). The parser accepts both forms for backwards compatibility, but the serializer outputs with parentheses.