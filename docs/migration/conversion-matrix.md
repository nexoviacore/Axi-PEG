# Conversion Matrix

This matrix tracks the executable Pascal capabilities covered by the AxPeg behavioral-parity conversion. A capability is not complete until its public compatibility contract, durable Ax application database effects, and captured side effects have parity evidence from a sanitized parity fixture.

| Legacy capability | AxPeg capability | Classification | Parity evidence |
| --- | --- | --- | --- |
| `CanInitiatePEG` | `CanInitiate` compatibility endpoint | Pending legacy-environment verification | Public-contract harness and fixture support established; run against the parity environment before routing traffic. |
| Transaction and primary-form lifecycle | Pending | Pending | Ticket #2 |
| Sequence, history, and outbound persistence | Pending | Pending | Ticket #3 |
| Grid, child transaction, tree, and image persistence | Pending | Pending | Ticket #4 |
| V1 workflow evaluation and task creation | Pending | Pending | Ticket #5 |
| PEG V2, grouped indexes, subtasks, and parameters | Pending | Pending | Ticket #6 |
| Task actions and integrations | Pending | Pending | Tickets #7-13 |

The complete method-level inventory will be expanded from the canonical Pascal modules as each migration slice is characterized. `uAxPEGActions 1.pas` remains the canonical action-module source until the UTF-8 duplicate is confirmed equivalent.
