# Iron Clad Support — NetLock Fork Scope

This repository is the IronCityIT-owned fork of upstream `0x101-Cyber-Security/NetLock-RMM` and is an active ICIT productization workspace.

## Product identity
- Client-facing product name: **Iron Clad Support**.
- Preserve upstream attribution/licensing in source and legal notices.
- Do not expose NetLock branding on client-facing UI where lawful and technically appropriate.

## Git / SDLC
- Work only on `productize/iron-clad-support` or feature branches from it.
- `origin` is `IronCityIT/NetLock-RMM`; `upstream` is the vendor repository.
- Never push directly to `main` and never rewrite upstream history.
- Keep upstream changes mergeable; prefer overlays/configuration and surgical patches over unnecessary divergence.

## Execution priorities
1. Alert quality, notification scoping, and suppression/deduplication.
2. Sensor semantics and remediation-capable states.
3. Automation/policy assignment and inventory-driven orchestration.
4. Remote-control reliability and safe relay behavior.
5. Iron City branding and operator UX.

## Validation
- Reproduce defects before patching where practical.
- Add regression tests for modified logic.
- Record failure → root cause → fix → validation.
- Do not claim success from command exit codes alone; verify functional behavior.
