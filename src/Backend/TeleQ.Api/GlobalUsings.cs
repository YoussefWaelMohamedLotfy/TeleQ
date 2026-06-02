// Make projection types from the shared library visible across the entire API project.
// This allows all existing endpoint files that reference BranchQueueSnapshot,
// QueueEntry, or BranchQueueProjection to continue working without modification.
global using TeleQ.Messaging.Shared.Projections;
