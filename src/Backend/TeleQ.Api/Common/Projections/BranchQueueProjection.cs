// QueueEntry, BranchQueueSnapshot and BranchQueueProjection have been moved to
// TeleQ.Messaging.Shared.Projections so that both the API and the Worker can
// register the projection in their own Marten configurations.
// A global using in GlobalUsings.cs re-exports them here for all API files.
namespace TeleQ.Api.Common.Projections;
