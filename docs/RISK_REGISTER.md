# Risk register

| Risk | Probability | Impact | Mitigation | Validation |
|---|---:|---:|---|---|
| Code collision | Low | Medium | Unique index and retries | Collision unit test |
| Database outage | Medium | High | Explicit 503 path; production HA DB | Failure-path test |
| Redirect abuse | Medium | High | Production rate limiting and monitoring | Load/security review |
| Malicious destination | Medium | High | HTTP(S) validation; no server-side fetch | Input tests |
| Analytics growth | Medium | Medium | Bounded metadata and retention policy | Schema review |
| AI-generated defect | Medium | High | Human review and automated tests | Quality gates |
| Requirement ambiguity | High | Medium | Assumptions and acceptance criteria | Design review |

