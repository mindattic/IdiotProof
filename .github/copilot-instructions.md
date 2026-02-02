# Copilot Instructions

## Project Guidelines
- User intends IdiotScript chained conditions to be evaluated sequentially (state machine over time), not as a single simultaneous AND condition.
- In IdiotProof, the backend doesn't load strategies directly; it reads strategies retrieved from the frontend, which gets them from .idiot files. The data flow is: .idiot files → Frontend → Backend.