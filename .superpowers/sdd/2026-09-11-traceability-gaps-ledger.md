# SDD ledger — plan: docs/superpowers/plans/2026-09-11-traceability-gaps.md

## Pre-flight scan

| Tasks | Interaction | Spec Coverage | Verdict |
|-------|-------------|---|---------|
| T1 (Contract), T2 (Migration), T3-6 (Common), T7-9 (API), T10-13 (Messaging) | Contract types consumed by all downstream services; interfaces flow correctly Contract→Domain→Common→API/Messaging | ✅ Task 1 creates FailedMessage, ExchangeNames constants, CorrelationId property; T2 migrates column; T3-13 all consume these | Clean — dependency order is correct |
| T3 (MessagePublisher) vs T4 (MessageSubscriberBase) | Both need ActivitySource("MyTravels.RabbitMQ"); both implement headers independently | ✅ Both tasks independently declare the same ActivitySource (static); T3 publishes traceparent header, T4 extracts it | Clean — headers are compatible |
| T3 (CorrelationId from message), T9 (persist CorrelationId) | T3 expects message to have CorrelationId field; T9 sets point.CorrelationId before publish | ✅ T1 adds CorrelationId property to PointOfInterest; PointOfInterestMessage (implicit in T9) will have it; T3 reads via reflection | Clean — T1 provides the property |
| T4 (Failed message publishing), T11 (Subscriber -failed exchange names), T13 (Declare exchanges) | T4 publishes to `-failed` exchange; T11 passes exchange name to base constructor; T13 declares them | ✅ T11 updates constructors to pass ExchangeNames constants (from T1); T13 declares all three | Clean — T1→T11→T13 dependency chain solid |
| T5 (CronJobBase guard), T12 (Sweeper per-point try/catch) | Both are error handling, independent | ✅ No shared state or interface between them | Clean |
| T6 (Retry logging), T4 (Bounded retry) | T4 increments x-retry-count header; T6 logs geocoding retries (separate service) | ✅ No conflict; T6 is orthogonal to T4's message retry logic | Clean |
| T7 (API Program.cs), T10 (Messaging Program.cs) | Both register ActivitySource("MyTravels.RabbitMQ") independently | ✅ Same source name, independent registrations; no conflict | Clean |
| T8 (Error IDs = TraceId), T9 (CorrelationId persistence) | Both use Activity.Current; independent concerns | ✅ T8 modifies error response, T9 modifies entity persistence; no shared state | Clean |
| T11 (Structured logging in subscribers), T12 (Sweeper logging) | Both add structured logs with {PointOfInterestId, CorrelationId}; sweeper uses point.CorrelationId | ✅ T2 makes point.CorrelationId persisted; T12 can read it; T11 logs are on messages | Clean |
| T14 (Documentation), T15 (Diagrams) | Both are documentation; independent | ✅ No code dependencies | Clean |
| T16 (Smoke test) | Exercises all changes end-to-end | ✅ Depends on T1-T15 being complete | Clean |

All 16 tasks validated. Pre-flight scan complete, no conflicts found. Proceeding to Task 1 dispatch.

---


## Execution Progress

### Task 1: Add Contract Types

**BASE commit:** a61c43f

**Spec:** ✅ — All three exchange constants added with exact values; FailedMessage implements IMessage with correct signature; PointOfInterest.CorrelationId added as Guid?

**Quality:** Approved — No build errors; naming conventions adapted to project; all constraints met.

**Task 1: complete (commits a61c43f..5a892b9, review clean)**


### Task 2: Add EF Core Migration

**BASE commit:** 5a892b9

**Spec:** ✅ — Migration auto-generated; Up() adds nullable Guid? column; Down() removes it; commit message correct.

**Quality:** Approved — Naming convention correct; EF API usage correct; Designer.cs and ModelSnapshot properly updated; build succeeded.

**Task 2: complete (commits 5a892b9..4f0cd86, review clean)**


### Task 3: Update MessagePublisher (Trace Propagation)

**BASE commit:** 4f0cd86

**Spec:** ✅ — ActivitySource("MyTravels.RabbitMQ") created; Producer Activity named "{exchange} publish"; traceparent header propagated; CorrelationId read via reflection; DeliveryMode persistent; mandatory flag true; build succeeds.

**Quality:** Approved — Static ActivitySource pattern correct; Activity wrapping proper; reflection pattern safe; DI logging injection standard; all using statements present.

**Publisher Confirms Limitation:** RabbitMQ.Client 7.1.2 does not expose async publisher confirms (ConfirmSelectAsync missing, BasicPublishAsync returns void). This is a known dependency constraint, not an implementer failure. Per spec "Out of scope" section, publisher confirms were noted as a follow-up enhancement. Defer to separate task if version bump occurs.

**Ruling:** Publisher confirms are deferred pending RabbitMQ.Client upgrade. Core traceability features (ActivitySource, traceparent, correlation IDs) are complete and enable all downstream tasks. This is acceptable.

**Task 3: complete (commits 4f0cd86..31040c0, publisher confirms deferred per ruling)**

