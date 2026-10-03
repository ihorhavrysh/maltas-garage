# Open items

Known gaps that are not fixed in code, with the reason. Each one either needs a decision,
costs money, or touches the live Azure setup.

## Azure

- **App connects to SQL as the server admin.** The connection string uses the admin login.
  Better: a contained database user with only `db_datareader`, `db_datawriter` and
  `db_ddladmin` (migrations), or a managed identity with Entra authentication and no password
  at all. Needs a change on the live server and a new connection string.
- **SQL firewall rule `AllowAzureServices` (0.0.0.0).** It admits any Azure service, from any
  tenant. Narrowing it to the web app's outbound IP addresses is possible on the free tier;
  a private endpoint needs a paid plan.
- **No monitoring or alerts.** Nothing reports a crash, a paused database or the F1 CPU quota.
  Application Insights has a free allowance; an external ping more often than hourly would keep
  the F1 app awake and use up its CPU quota.
- **Infrastructure as code is not deployed.** `infra/main.bicep` describes the current resources
  and can be checked with `az deployment group what-if`; CI does not deploy it yet.
- **The free database pauses for the rest of the month** once its 100,000 vCore-seconds are
  used. The site then shows a "paused until the 1st" page. Switching the database to paid
  billing when the allowance runs out is a billing decision, so it is not made in code.

## Data model

- **No foreign key from `UserProfiles.UserId` to `AspNetUsers.Id`.** Consistency is kept by
  code. A deleted account keeps its profile with `UserId = deleted_{Id}` so the other party's
  orders and reviews still resolve. The alternative is a nullable `UserId` with
  `ON DELETE SET NULL`; that changes how deleted accounts are recognised everywhere.
- **Random GUID keys.** About 40 places set `Id = Guid.NewGuid()`. Sequential GUIDs from EF would
  fragment the clustered indexes less; at this data size it does not matter.
- **Notifications save the caller's pending changes.** `IMessagingService` methods call
  `SaveChangesAsync`, so callers write first and notify afterwards (documented on the interface).
  A notification outbox would make the two independent.
