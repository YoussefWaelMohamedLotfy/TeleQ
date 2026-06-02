using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TeleQ.Messaging.Shared.Aggregates;
using TeleQ.Messaging.Shared.Configuration;
using TeleQ.Messaging.Shared.DomainEvents;
using TeleQ.Messaging.Worker.Data;
using TeleQ.Messaging.Worker.Data.Entities;
using TeleQ.Messaging.Worker.Helpers;
using IDocumentSession = Marten.IDocumentSession;
using IQuerySession = Marten.IQuerySession;

namespace TeleQ.Messaging.Worker.Telegram;

/// <summary>
/// Stateful singleton that processes every Telegram <see cref="Update"/> received via long-polling.
/// </summary>
public sealed partial class TelegramUpdateHandler(
    ILogger<TelegramUpdateHandler> logger,
    IServiceScopeFactory scopeFactory,
    HybridCache cache,
    IOptions<TelegramBotOptions> telegramOptions) : IUpdateHandler
{
    private static readonly Regex PhoneRegex = GetPhoneRegex();

    private readonly ConcurrentDictionary<long, ChatContext> _chatContexts = new();

    // ── IUpdateHandler implementation ──────────────────────────────────────

    public async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        try
        {
            switch (update)
            {
                case { Message: not null }:
                    await HandleMessageAsync(client, update.Message, ct);
                    break;
                case { CallbackQuery: not null }:
                    await HandleCallbackQueryAsync(client, update.CallbackQuery, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram update handling failed for update {UpdateId}", update.Id);

            var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
            if (chatId.HasValue)
            {
                await SafeSendMessageAsync(client, chatId.Value,
                    ex is InvalidOperationException ? ex.Message : "Sorry, something went wrong. Please try again.", ct);
            }

            if (update.CallbackQuery is not null)
            {
                await SafeAnswerCallbackAsync(client, update.CallbackQuery.Id, ct);
            }
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        logger.LogError(exception, "Telegram Bot polling error");
        return Task.CompletedTask;
    }

    // ── Message routing ────────────────────────────────────────────────────

    private async Task HandleMessageAsync(ITelegramBotClient client, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        await TouchCustomerActivityAsync(chatId, ct);

        var payload = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(payload) && string.IsNullOrWhiteSpace(message.Contact?.PhoneNumber))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(payload) && payload.StartsWith('/'))
        {
            var command = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant().TrimStart('/');
            logger.LogDebug("Telegram command '{Command}' from chat {ChatId}", command, chatId);

            switch (command)
            {
                case "start":
                    ClearContext(chatId);
                    await HandleStartAsync(client, message, ct);
                    return;
                case "book":
                    await HandleBookAsync(client, chatId, ct);
                    return;
                case "appointment":
                    await HandleAppointmentAsync(client, chatId, ct);
                    return;
                case "status":
                    await HandleStatusAsync(client, chatId, ct);
                    return;
                case "cancel":
                    await HandleCancelAsync(client, chatId, ct);
                    return;
                case "help":
                    ClearContext(chatId);
                    await client.SendMessage(chatId, GetHelpText(), cancellationToken: ct);
                    return;
                default:
                    await client.SendMessage(chatId, "Unknown command. Type /help for available commands.", cancellationToken: ct);
                    return;
            }
        }

        var context = GetContext(chatId);
        if (context.Step == ConversationStep.AwaitingPhoneNumber)
        {
            await HandlePhoneNumberAsync(client, message, context, ct);
            return;
        }

        if (context.Step != ConversationStep.Idle)
        {
            await client.SendMessage(chatId, "Please use the buttons above to continue, or send /help to start over.", cancellationToken: ct);
        }
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient client, CallbackQuery callbackQuery, CancellationToken ct)
    {
        await SafeAnswerCallbackAsync(client, callbackQuery.Id, ct);

        var chatId = callbackQuery.Message?.Chat.Id;
        if (!chatId.HasValue || string.IsNullOrWhiteSpace(callbackQuery.Data))
        {
            return;
        }

        await TouchCustomerActivityAsync(chatId.Value, ct);

        var separatorIndex = callbackQuery.Data.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == callbackQuery.Data.Length - 1)
        {
            throw new InvalidOperationException("That action is no longer valid. Please start again.");
        }

        var action = callbackQuery.Data[..separatorIndex];
        var idPart = callbackQuery.Data[(separatorIndex + 1)..];

        if (!Guid.TryParse(idPart, out var entityId))
        {
            throw new InvalidOperationException("That selection is invalid. Please try again.");
        }

        switch (action)
        {
            case "branch_book":
                await HandleBranchSelectionAsync(client, callbackQuery, entityId, isAppointment: false, ct);
                break;
            case "service_book":
                await HandleServiceSelectionForBookingAsync(client, callbackQuery, entityId, ct);
                break;
            case "branch_apt":
                await HandleBranchSelectionAsync(client, callbackQuery, entityId, isAppointment: true, ct);
                break;
            case "service_apt":
                await HandleServiceSelectionForAppointmentAsync(client, callbackQuery, entityId, ct);
                break;
            case "slot_apt":
                await HandleSlotSelectionAsync(client, callbackQuery, entityId, ct);
                break;
            default:
                throw new InvalidOperationException("That action is not supported. Please start again.");
        }
    }

    // ── Command handlers ───────────────────────────────────────────────────

    private async Task HandleStartAsync(ITelegramBotClient client, Message message, CancellationToken ct)
    {
        var name = message.From?.FirstName ?? "there";
        var existingCustomer = await GetCustomerByChatIdAsync(message.Chat.Id, ct);
        var registrationLine = existingCustomer is null
            ? "I'll ask for your phone number whenever it is needed."
            : $"Your registered phone number is {existingCustomer.PhoneNumber}.";

        var response = $"""
            👋 Hello {name}! Welcome to TeleQ.

            I can help you manage your queue tickets:
            /book — Get a walk-in ticket
            /appointment — Book an appointment slot
            /status — Check your active ticket status
            /cancel — Cancel your active waiting ticket
            /help — Show help

            {registrationLine}
            """;

        await client.SendMessage(message.Chat.Id, response, cancellationToken: ct);
    }

    private async Task HandleBookAsync(ITelegramBotClient client, long chatId, CancellationToken ct)
    {
        var branches = await GetActiveBranchesAsync(ct);
        if (branches.Count == 0)
        {
            await client.SendMessage(chatId, "No active branches are available right now.", cancellationToken: ct);
            return;
        }

        SetContext(chatId, new ChatContext
        {
            Step = ConversationStep.AwaitingBranchSelection,
            PendingCommand = "book"
        });

        await client.SendMessage(
            chatId,
            "📋 Select a branch for your walk-in ticket:",
            replyMarkup: BranchKeyboard(branches, "branch_book"),
            cancellationToken: ct);
    }

    private async Task HandleAppointmentAsync(ITelegramBotClient client, long chatId, CancellationToken ct)
    {
        var branches = await GetActiveBranchesAsync(ct);
        if (branches.Count == 0)
        {
            await client.SendMessage(chatId, "No active branches are available right now.", cancellationToken: ct);
            return;
        }

        SetContext(chatId, new ChatContext
        {
            Step = ConversationStep.AwaitingBranchSelection,
            PendingCommand = "appointment"
        });

        await client.SendMessage(
            chatId,
            "📅 Select a branch for your appointment:",
            replyMarkup: BranchKeyboard(branches, "branch_apt"),
            cancellationToken: ct);
    }

    private async Task HandleStatusAsync(ITelegramBotClient client, long chatId, CancellationToken ct)
    {
        var customer = await GetCustomerByChatIdAsync(chatId, ct);
        if (customer is null)
        {
            SetContext(chatId, new ChatContext
            {
                Step = ConversationStep.AwaitingPhoneNumber,
                PendingCommand = "status"
            });

            await client.SendMessage(chatId, "Please send your phone number (e.g. +201234567890)", cancellationToken: ct);
            return;
        }

        ClearContext(chatId);
        await client.SendMessage(chatId, await BuildStatusMessageAsync(customer.PhoneNumber, ct), cancellationToken: ct);
    }

    private async Task HandleCancelAsync(ITelegramBotClient client, long chatId, CancellationToken ct)
    {
        var customer = await GetCustomerByChatIdAsync(chatId, ct);
        if (customer is null)
        {
            SetContext(chatId, new ChatContext
            {
                Step = ConversationStep.AwaitingPhoneNumber,
                PendingCommand = "cancel"
            });

            await client.SendMessage(chatId, "Please send your phone number (e.g. +201234567890)", cancellationToken: ct);
            return;
        }

        ClearContext(chatId);
        var msg = await CancelActiveTicketAsync(customer.PhoneNumber, ct);
        await client.SendMessage(chatId, msg, cancellationToken: ct);
    }

    // ── Callback (inline-keyboard) handlers ────────────────────────────────

    private async Task HandleBranchSelectionAsync(
        ITelegramBotClient client,
        CallbackQuery callbackQuery,
        Guid branchId,
        bool isAppointment,
        CancellationToken ct)
    {
        var branchTask = cache.GetOrCreateAsync<Branch?>(
            CacheKeys.BranchEntity(branchId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == branchId && x.IsActive, innerCt);
            },
            CacheOptions.Static,
            CacheKeys.BranchTags(branchId),
            ct).AsTask();

        var servicesTask = cache.GetOrCreateAsync<List<Service>>(
            CacheKeys.ServiceListEntities(branchId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.Services
                    .AsNoTracking()
                    .Where(x => x.BranchId == branchId && x.IsActive)
                    .OrderBy(x => x.Name)
                    .ToListAsync(innerCt);
            },
            CacheOptions.Static,
            CacheKeys.ServiceListTags(branchId),
            ct).AsTask();

        await Task.WhenAll(branchTask, servicesTask);

        var branch = await branchTask
            ?? throw new InvalidOperationException("The selected branch is not available.");
        var services = await servicesTask;

        if (services.Count == 0)
        {
            throw new InvalidOperationException($"{branch.Name} has no active services right now.");
        }

        SetContext(callbackQuery.Message!.Chat.Id, new ChatContext
        {
            Step = ConversationStep.AwaitingServiceSelection,
            SelectedBranchId = branchId,
            PendingCommand = isAppointment ? "appointment" : "book"
        });

        await UpdateCallbackMessageAsync(
            client,
            callbackQuery,
            $"Select a service at {branch.Name}:",
            ServiceKeyboard(services, isAppointment ? "service_apt" : "service_book"),
            ct);
    }

    private async Task HandleServiceSelectionForBookingAsync(
        ITelegramBotClient client,
        CallbackQuery callbackQuery,
        Guid serviceId,
        CancellationToken ct)
    {
        var chatId = callbackQuery.Message!.Chat.Id;
        var serviceTask = cache.GetOrCreateAsync<CachedServiceInfo?>(
            CacheKeys.ServiceWithBranch(serviceId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.Services
                    .AsNoTracking()
                    .Where(x => x.Id == serviceId && x.IsActive && x.Branch.IsActive)
                    .Select(x => new CachedServiceInfo(x.Id, x.BranchId, x.Name, x.Branch.Name))
                    .FirstOrDefaultAsync(innerCt);
            },
            CacheOptions.Static,
            CacheKeys.ServiceWithBranchTags(serviceId),
            ct).AsTask();
        var customerTask = GetCustomerByChatIdAsync(chatId, ct).AsTask();
        await Task.WhenAll(serviceTask, customerTask);

        var service = await serviceTask
            ?? throw new InvalidOperationException("The selected service is not available.");
        var customer = await customerTask;
        if (customer is null)
        {
            SetContext(chatId, new ChatContext
            {
                Step = ConversationStep.AwaitingPhoneNumber,
                SelectedBranchId = service.BranchId,
                SelectedServiceId = service.Id,
                PendingCommand = "book"
            });

            await UpdateCallbackMessageAsync(
                client,
                callbackQuery,
                $"{service.Name} selected at {service.BranchName}. Please send your phone number (e.g. +201234567890)",
                null,
                ct);
            return;
        }

        var ticket = await IssueWalkInTicketAsync(service.BranchId, service.Id, customer.PhoneNumber, ct);
        ClearContext(chatId);

        await UpdateCallbackMessageAsync(
            client,
            callbackQuery,
            BuildWalkInConfirmationMessage(service.BranchName, service.Name, ticket),
            null,
            ct,
            ParseMode.Html);
    }

    private async Task HandleServiceSelectionForAppointmentAsync(
        ITelegramBotClient client,
        CallbackQuery callbackQuery,
        Guid serviceId,
        CancellationToken ct)
    {
        var service = await cache.GetOrCreateAsync<CachedServiceInfo?>(
            CacheKeys.ServiceWithBranch(serviceId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.Services
                    .AsNoTracking()
                    .Where(x => x.Id == serviceId && x.IsActive && x.Branch.IsActive)
                    .Select(x => new CachedServiceInfo(x.Id, x.BranchId, x.Name, x.Branch.Name))
                    .FirstOrDefaultAsync(innerCt);
            },
            CacheOptions.Static,
            CacheKeys.ServiceWithBranchTags(serviceId),
            ct)
            ?? throw new InvalidOperationException("The selected service is not available.");

        var cachedSlots = await cache.GetOrCreateAsync<List<TimeSlot>>(
            CacheKeys.AvailableTimeSlots(service.Id),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.TimeSlots
                    .AsNoTracking()
                    .Where(x => x.BranchId == service.BranchId && x.ServiceId == service.Id && x.IsActive && x.BookedCount < x.Capacity)
                    .ToListAsync(innerCt);
            },
            CacheOptions.Queue,
            CacheKeys.TimeSlotListTags(service.Id),
            ct);

        var slots = cachedSlots
            .Where(x => SlotScheduler.NextOccurrence(x) > DateTimeOffset.UtcNow)
            .OrderBy(SlotScheduler.NextOccurrence)
            .ToList();

        if (slots.Count == 0)
        {
            throw new InvalidOperationException($"No available time slots were found for {service.Name}.");
        }

        SetContext(callbackQuery.Message!.Chat.Id, new ChatContext
        {
            Step = ConversationStep.AwaitingSlotSelection,
            SelectedBranchId = service.BranchId,
            SelectedServiceId = service.Id,
            PendingCommand = "appointment"
        });

        await UpdateCallbackMessageAsync(
            client,
            callbackQuery,
            $"Select a time slot for {service.Name} at {service.BranchName}:",
            SlotKeyboard(slots),
            ct);
    }

    private async Task HandleSlotSelectionAsync(
        ITelegramBotClient client,
        CallbackQuery callbackQuery,
        Guid slotId,
        CancellationToken ct)
    {
        var slot = await cache.GetOrCreateAsync<TimeSlot?>(
            CacheKeys.TimeSlotEntity(slotId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.TimeSlots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == slotId && x.IsActive, innerCt);
            },
            CacheOptions.Queue,
            ["timeslots", $"timeslot:{slotId}"],
            ct)
            ?? throw new InvalidOperationException("The selected time slot is not available.");

        if (slot.BookedCount >= slot.Capacity || SlotScheduler.NextOccurrence(slot) <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The selected time slot is no longer available.");
        }

        var chatId = callbackQuery.Message!.Chat.Id;
        var serviceTask = cache.GetOrCreateAsync<CachedServiceInfo?>(
            CacheKeys.ServiceWithBranch(slot.ServiceId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.Services
                    .AsNoTracking()
                    .Where(x => x.Id == slot.ServiceId && x.IsActive && x.Branch.IsActive)
                    .Select(x => new CachedServiceInfo(x.Id, x.BranchId, x.Name, x.Branch.Name))
                    .FirstOrDefaultAsync(innerCt);
            },
            CacheOptions.Static,
            CacheKeys.ServiceWithBranchTags(slot.ServiceId),
            ct).AsTask();
        var customerTask = GetCustomerByChatIdAsync(chatId, ct).AsTask();
        await Task.WhenAll(serviceTask, customerTask);

        var service = await serviceTask
            ?? throw new InvalidOperationException("The selected service is not available.");
        var customer = await customerTask;
        if (customer is null)
        {
            SetContext(chatId, new ChatContext
            {
                Step = ConversationStep.AwaitingPhoneNumber,
                SelectedBranchId = slot.BranchId,
                SelectedServiceId = slot.ServiceId,
                SelectedTimeSlotId = slot.Id,
                PendingCommand = "appointment"
            });

            await UpdateCallbackMessageAsync(
                client,
                callbackQuery,
                $"Slot selected for {FormatSlot(slot)}. Please send your phone number (e.g. +201234567890)",
                null,
                ct);
            return;
        }

        var ticket = await BookAppointmentAsync(slot.BranchId, slot.ServiceId, slot.Id, customer.PhoneNumber, ct);
        ClearContext(chatId);

        await UpdateCallbackMessageAsync(
            client,
            callbackQuery,
            BuildAppointmentConfirmationMessage(service.BranchName, service.Name, slot, ticket),
            null,
            ct,
            ParseMode.Html);
    }

    private async Task HandlePhoneNumberAsync(
        ITelegramBotClient client,
        Message message,
        ChatContext context,
        CancellationToken ct)
    {
        var phone = NormalizePhoneNumber(message.Contact?.PhoneNumber ?? message.Text);
        if (phone is null)
        {
            await client.SendMessage(message.Chat.Id, "That phone number looks invalid. Please send it in this format: +201234567890", cancellationToken: ct);
            return;
        }

        await SaveCustomerAsync(message.Chat.Id, phone, ct);

        switch (context.PendingCommand)
        {
            case "book" when context.SelectedBranchId.HasValue && context.SelectedServiceId.HasValue:
                {
                    var details = await GetServiceDetailsAsync(context.SelectedBranchId.Value, context.SelectedServiceId.Value, ct);
                    var ticket = await IssueWalkInTicketAsync(context.SelectedBranchId.Value, context.SelectedServiceId.Value, phone, ct);
                    ClearContext(message.Chat.Id);
                    await client.SendMessage(message.Chat.Id, BuildWalkInConfirmationMessage(details.BranchName, details.ServiceName, ticket), parseMode: ParseMode.Html, cancellationToken: ct);
                    return;
                }
            case "appointment" when context.SelectedBranchId.HasValue && context.SelectedServiceId.HasValue && context.SelectedTimeSlotId.HasValue:
                {
                    var details = await GetServiceDetailsAsync(context.SelectedBranchId.Value, context.SelectedServiceId.Value, ct);
                    var slot = await GetTimeSlotAsync(context.SelectedTimeSlotId.Value, ct)
                        ?? throw new InvalidOperationException("The selected time slot could not be found.");
                    var ticket = await BookAppointmentAsync(context.SelectedBranchId.Value, context.SelectedServiceId.Value, context.SelectedTimeSlotId.Value, phone, ct);
                    ClearContext(message.Chat.Id);
                    await client.SendMessage(message.Chat.Id, BuildAppointmentConfirmationMessage(details.BranchName, details.ServiceName, slot, ticket), parseMode: ParseMode.Html, cancellationToken: ct);
                    return;
                }
            case "status":
                ClearContext(message.Chat.Id);
                await client.SendMessage(message.Chat.Id, await BuildStatusMessageAsync(phone, ct), cancellationToken: ct);
                return;
            case "cancel":
                ClearContext(message.Chat.Id);
                await client.SendMessage(message.Chat.Id, await CancelActiveTicketAsync(phone, ct), cancellationToken: ct);
                return;
            default:
                ClearContext(message.Chat.Id);
                await client.SendMessage(message.Chat.Id, $"✅ Phone number {phone} saved. You can now use /book, /appointment, /status, or /cancel.", cancellationToken: ct);
                return;
        }
    }

    // ── Data access helpers ────────────────────────────────────────────────

    private ValueTask<TelegramCustomer?> GetCustomerByChatIdAsync(long chatId, CancellationToken ct) =>
        cache.GetOrCreateAsync<TelegramCustomer?>(
            CacheKeys.TelegramCustomer(chatId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.TelegramCustomers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TelegramChatId == chatId, innerCt);
            },
            CacheOptions.Customer,
            CacheKeys.TelegramCustomerTags(chatId),
            ct);

    private async Task SaveCustomerAsync(long chatId, string phoneNumber, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var now = DateTimeOffset.UtcNow;

        var byChat = await db.TelegramCustomers.FirstOrDefaultAsync(x => x.TelegramChatId == chatId, ct);
        var byPhone = await db.TelegramCustomers.FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber, ct);

        if (byChat is not null && byPhone is not null && byChat.Id != byPhone.Id)
        {
            db.TelegramCustomers.Remove(byChat);
            byChat = null;
        }

        var customer = byChat ?? byPhone;
        if (customer is null)
        {
            customer = new TelegramCustomer
            {
                Id = Guid.CreateVersion7(),
                TelegramChatId = chatId,
                PhoneNumber = phoneNumber,
                RegisteredAt = now,
                LastActiveAt = now
            };
            db.TelegramCustomers.Add(customer);
        }
        else
        {
            customer.TelegramChatId = chatId;
            customer.PhoneNumber = phoneNumber;
            customer.LastActiveAt = now;
        }

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.TelegramCustomerTags(chatId), ct);
    }

    private async Task TouchCustomerActivityAsync(long chatId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var customer = await db.TelegramCustomers.FirstOrDefaultAsync(x => x.TelegramChatId == chatId, ct);
        if (customer is null)
        {
            return;
        }

        customer.LastActiveAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private ValueTask<IList<Branch>> GetActiveBranchesAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IList<Branch>>(
            CacheKeys.BranchListEntities(),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.Branches
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Name)
                    .ToListAsync(innerCt);
            },
            CacheOptions.Static,
            CacheKeys.BranchListTags(),
            ct);

    private async Task<(string BranchName, string ServiceName)> GetServiceDetailsAsync(Guid branchId, Guid serviceId, CancellationToken ct)
    {
        var service = await cache.GetOrCreateAsync<CachedServiceInfo?>(
            CacheKeys.ServiceWithBranch(serviceId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.Services
                    .AsNoTracking()
                    .Where(x => x.Id == serviceId && x.BranchId == branchId)
                    .Select(x => new CachedServiceInfo(x.Id, x.BranchId, x.Name, x.Branch.Name))
                    .FirstOrDefaultAsync(innerCt);
            },
            CacheOptions.Static,
            CacheKeys.ServiceWithBranchTags(serviceId),
            ct)
            ?? throw new InvalidOperationException("The selected service could not be found.");

        return (service.BranchName, service.Name);
    }

    private ValueTask<TimeSlot?> GetTimeSlotAsync(Guid slotId, CancellationToken ct) =>
        cache.GetOrCreateAsync<TimeSlot?>(
            CacheKeys.TimeSlotEntity(slotId),
            async innerCt =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
                return await db.TimeSlots
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == slotId, innerCt);
            },
            CacheOptions.Queue,
            ["timeslots", $"timeslot:{slotId}"],
            ct);

    private async Task<Ticket> IssueWalkInTicketAsync(Guid branchId, Guid serviceId, string customerPhone, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        var queueId = GetQueueId(branchId, serviceId);
        var serviceTask = db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serviceId && x.BranchId == branchId && x.IsActive, ct);
        var queueTask = session.LoadAsync<BranchQueueSnapshot>(queueId, ct);
        await Task.WhenAll(serviceTask, queueTask);

        _ = await serviceTask
            ?? throw new InvalidOperationException("Service not found or inactive for the selected branch.");
        var queue = await queueTask;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var queuePosition = (queue is null || queue.LastQueueDate < today) ? 1 : queue.NextQueueNumber;
        var ticketNumber = $"A-{queuePosition:D3}";
        var ticketId = Guid.CreateVersion7();

        var evt = new TicketIssued(
            TicketId: ticketId,
            TicketNumber: ticketNumber,
            CustomerPhone: customerPhone,
            BranchId: branchId,
            ServiceId: serviceId,
            QueuePosition: queuePosition,
            IssuedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Ticket>(ticketId, evt);
        await session.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync(CacheKeys.QueueTags(branchId, serviceId), ct);

        return await session.Events.AggregateStreamAsync<Ticket>(ticketId, token: ct)
            ?? throw new InvalidOperationException("The ticket could not be created.");
    }

    private async Task<Ticket> BookAppointmentAsync(Guid branchId, Guid serviceId, Guid timeSlotId, string customerPhone, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        var slot = await db.TimeSlots.FirstOrDefaultAsync(x => x.Id == timeSlotId, ct)
            ?? throw new InvalidOperationException("The selected time slot could not be found.");

        if (!slot.IsActive || slot.ServiceId != serviceId || slot.BranchId != branchId)
        {
            throw new InvalidOperationException("Time slot not found or inactive for the selected branch and service.");
        }

        if (slot.BookedCount >= slot.Capacity)
        {
            throw new InvalidOperationException("This time slot is fully booked.");
        }

        var scheduledAt = SlotScheduler.NextOccurrence(slot);
        if (scheduledAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Cannot book a time slot in the past.");
        }

        var queueId = GetQueueId(branchId, serviceId);
        var queue = await session.LoadAsync<BranchQueueSnapshot>(queueId, ct);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var queuePosition = (queue is null || queue.LastQueueDate < today) ? 1 : queue.NextQueueNumber;
        var ticketNumber = $"B-{queuePosition:D3}";
        var ticketId = Guid.CreateVersion7();

        var evt = new AppointmentBooked(
            TicketId: ticketId,
            TicketNumber: ticketNumber,
            CustomerPhone: customerPhone,
            BranchId: branchId,
            ServiceId: serviceId,
            TimeSlotId: timeSlotId,
            ScheduledAt: scheduledAt,
            QueuePosition: queuePosition,
            BookedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Ticket>(ticketId, evt);
        slot.BookedCount++;

        await db.SaveChangesAsync(ct);
        await session.SaveChangesAsync(ct);

        await cache.RemoveByTagAsync(
            [.. CacheKeys.QueueTags(branchId, serviceId), "timeslots", $"timeslots:service:{serviceId}", $"timeslot:{timeSlotId}"], ct);

        return await session.Events.AggregateStreamAsync<Ticket>(ticketId, token: ct)
            ?? throw new InvalidOperationException("The appointment could not be created.");
    }

    private async Task<string> BuildStatusMessageAsync(string phoneNumber, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var querySession = scope.ServiceProvider.GetRequiredService<IQuerySession>();

        var snapshotsTask = Marten.QueryableExtensions.ToListAsync(querySession.Query<BranchQueueSnapshot>(), ct);
        var branches = await db.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var services = await db.Services.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x, ct);
        var snapshots = await snapshotsTask;

        var activeEntries = snapshots
            .SelectMany(snapshot => snapshot.WaitingTickets.Concat(snapshot.CalledTickets)
                .Where(entry => entry.CustomerPhone == phoneNumber)
                .Select(entry => new { snapshot, entry }))
            .OrderBy(x => x.entry.QueuePosition)
            .ToList();

        if (activeEntries.Count == 0)
        {
            return "You have no active tickets right now.";
        }

        var lines = new List<string> { "🎫 Your active tickets:" };

        var ticketResults = await Task.WhenAll(activeEntries.Select(async item =>
        {
            await using var ticketScope = scopeFactory.CreateAsyncScope();
            var qs = ticketScope.ServiceProvider.GetRequiredService<IQuerySession>();
            var ticket = await qs.Events.AggregateStreamAsync<Ticket>(item.entry.TicketId, token: ct);
            return (item, ticket);
        }));

        foreach (var (item, ticket) in ticketResults)
        {
            if (ticket is null || ticket.Status is TicketStatus.Served or TicketStatus.NoShow or TicketStatus.Cancelled)
            {
                continue;
            }

            branches.TryGetValue(ticket.BranchId, out var branchName);
            services.TryGetValue(ticket.ServiceId, out var service);
            var aheadCount = item.snapshot.WaitingTickets.Count(x => x.QueuePosition < ticket.QueuePosition);
            var estimatedWait = aheadCount * (service?.EstimatedDurationMinutes ?? 10);
            var statusLine = ticket.Status == TicketStatus.Called
                ? $"Called to {ticket.CounterLabel ?? "counter"}"
                : $"Waiting • {aheadCount} ahead • ~{estimatedWait} min";
            var scheduleLine = ticket.ScheduledAt.HasValue
                ? $" • {ticket.ScheduledAt.Value:ddd dd MMM HH:mm}"
                : string.Empty;

            lines.Add($"• {ticket.TicketNumber} — {branchName ?? "Unknown branch"} / {service?.Name ?? "Unknown service"} — {statusLine}{scheduleLine}");
        }

        return lines.Count == 1
            ? "You have no active tickets right now."
            : string.Join(Environment.NewLine, lines);
    }

    private async Task<string> CancelActiveTicketAsync(string phoneNumber, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        var snapshots = await Marten.QueryableExtensions.ToListAsync(session.Query<BranchQueueSnapshot>(), ct);
        var waitingTicketId = snapshots
            .SelectMany(x => x.WaitingTickets)
            .Where(x => x.CustomerPhone == phoneNumber)
            .OrderBy(x => x.QueuePosition)
            .Select(x => x.TicketId)
            .FirstOrDefault();

        if (waitingTicketId == Guid.Empty)
        {
            return "You have no active waiting ticket to cancel.";
        }

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(waitingTicketId, token: ct)
            ?? throw new InvalidOperationException("The ticket could not be found.");

        if (ticket.CustomerPhone != phoneNumber || ticket.Status != TicketStatus.Waiting)
        {
            return "You have no active waiting ticket to cancel.";
        }

        if (ticket.TimeSlotId.HasValue)
        {
            var slot = await db.TimeSlots.FirstOrDefaultAsync(x => x.Id == ticket.TimeSlotId.Value, ct);
            slot?.BookedCount = Math.Max(0, slot.BookedCount - 1);
        }

        var evt = new TicketCancelled(
            TicketId: ticket.Id,
            BranchId: ticket.BranchId,
            ServiceId: ticket.ServiceId,
            CancelledBy: "telegram-bot",
            CancelledAt: DateTimeOffset.UtcNow);

        session.Events.Append(ticket.Id, evt);
        await db.SaveChangesAsync(ct);
        await session.SaveChangesAsync(ct);

        var queueTags = CacheKeys.QueueTags(ticket.BranchId, ticket.ServiceId);
        await cache.RemoveByTagAsync(
            ticket.TimeSlotId.HasValue
                ? [.. queueTags, "timeslots", $"timeslots:service:{ticket.ServiceId}", $"timeslot:{ticket.TimeSlotId.Value}"]
                : [.. queueTags], ct);

        return $"❌ Ticket {ticket.TicketNumber} has been cancelled.";
    }

    // ── UI helpers ─────────────────────────────────────────────────────────

    private static InlineKeyboardMarkup BranchKeyboard(IList<Branch> branches, string prefix) =>
        new(branches
            .Select(branch => new[]
            {
                InlineKeyboardButton.WithCallbackData(branch.Name, $"{prefix}:{branch.Id}")
            }));

    private static InlineKeyboardMarkup ServiceKeyboard(IList<Service> services, string prefix) =>
        new(services
            .Select(service => new[]
            {
                InlineKeyboardButton.WithCallbackData(service.Name, $"{prefix}:{service.Id}")
            }));

    private static InlineKeyboardMarkup SlotKeyboard(IList<TimeSlot> slots) =>
        new(slots
            .OrderBy(SlotScheduler.NextOccurrence)
            .Select(slot => new[]
            {
                InlineKeyboardButton.WithCallbackData(FormatSlot(slot), $"slot_apt:{slot.Id}")
            }));

    private string BuildWalkInConfirmationMessage(string branchName, string serviceName, Ticket ticket)
    {
        var message = $"""
            ✅ Walk-in ticket issued.
            Ticket: {HtmlEscape(ticket.TicketNumber)}
            Branch: {HtmlEscape(branchName)}
            Service: {HtmlEscape(serviceName)}
            Queue position: {ticket.QueuePosition}
            """;

        var baseUrl = telegramOptions.Value.FrontendBaseUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl)
            ? message
            : $"{message}\n<a href=\"{baseUrl}/ticket/{ticket.Id}\">🔗 View your ticket</a>";
    }

    private string BuildAppointmentConfirmationMessage(string branchName, string serviceName, TimeSlot slot, Ticket ticket)
    {
        var message = $"""
            ✅ Appointment booked.
            Ticket: {HtmlEscape(ticket.TicketNumber)}
            Branch: {HtmlEscape(branchName)}
            Service: {HtmlEscape(serviceName)}
            Slot: {HtmlEscape(FormatSlot(slot))}
            Queue position: {ticket.QueuePosition}
            """;

        var baseUrl = telegramOptions.Value.FrontendBaseUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(baseUrl)
            ? message
            : $"{message}\n<a href=\"{baseUrl}/ticket/{ticket.Id}\">🔗 View your ticket</a>";
    }

    private static string HtmlEscape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string FormatSlot(TimeSlot slot)
    {
        var scheduledAt = SlotScheduler.NextOccurrence(slot);
        var remaining = Math.Max(0, slot.Capacity - slot.BookedCount);
        return $"{scheduledAt:ddd dd MMM HH:mm} ({remaining} left)";
    }

    private async Task UpdateCallbackMessageAsync(
        ITelegramBotClient client,
        CallbackQuery callbackQuery,
        string text,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken ct,
        ParseMode? parseMode = null)
    {
        if (callbackQuery.Message is null)
        {
            await client.SendMessage(callbackQuery.From.Id, text, parseMode: parseMode ?? default, replyMarkup: replyMarkup, cancellationToken: ct);
            return;
        }

        await client.EditMessageText(
            callbackQuery.Message.Chat.Id,
            callbackQuery.Message.MessageId,
            text,
            parseMode: parseMode ?? default,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    private static string? NormalizePhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

        return PhoneRegex.IsMatch(normalized) ? normalized : null;
    }

    private static string GetQueueId(Guid branchId, Guid serviceId) => $"{branchId}:{serviceId}";

    private ChatContext GetContext(long chatId) => _chatContexts.TryGetValue(chatId, out var context) ? context : new ChatContext();

    private void SetContext(long chatId, ChatContext context) => _chatContexts[chatId] = context;

    private void ClearContext(long chatId) => _chatContexts.TryRemove(chatId, out _);

    private static string GetHelpText() =>
        """
        TeleQ Bot Commands:
        /start — Welcome message
        /book — Get a walk-in ticket
        /appointment — Book an appointment slot
        /status — Check active tickets
        /cancel — Cancel your active waiting ticket
        /help — Show this help
        """;

    private static async Task SafeSendMessageAsync(ITelegramBotClient client, long chatId, string message, CancellationToken ct)
    {
        try
        {
            await client.SendMessage(chatId, message, cancellationToken: ct);
        }
        catch
        {
            // ignore secondary delivery failures
        }
    }

    private static async Task SafeAnswerCallbackAsync(ITelegramBotClient client, string callbackQueryId, CancellationToken ct)
    {
        try
        {
            await client.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct);
        }
        catch
        {
            // ignore secondary delivery failures
        }
    }

    [GeneratedRegex("^\\+?\\d{8,15}$", RegexOptions.Compiled)]
    private static partial Regex GetPhoneRegex();
}

// ── Conversation state ─────────────────────────────────────────────────────

/// <summary>
/// Read model entry for a single ticket in a queue snapshot.
/// Mirrors the API projection document; used for status queries by the Worker.
/// </summary>
public sealed class QueueEntry
{
    public Guid TicketId { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
}

/// <summary>
/// Live queue snapshot document — read from Marten (projected and maintained by TeleQ.Api).
/// The Worker queries this document to determine queue position for status reports and ticket issuance.
/// </summary>
public sealed class BranchQueueSnapshot
{
    public string Id { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Guid ServiceId { get; set; }
    public List<QueueEntry> WaitingTickets { get; set; } = [];
    public List<QueueEntry> CalledTickets { get; set; } = [];
    public int NextQueueNumber { get; set; } = 1;
    public DateOnly LastQueueDate { get; set; }
}

public sealed record ChatContext
{
    public ConversationStep Step { get; init; } = ConversationStep.Idle;
    public Guid? SelectedBranchId { get; init; }
    public Guid? SelectedServiceId { get; init; }
    public Guid? SelectedTimeSlotId { get; init; }
    public string? PendingCommand { get; init; }
}

public enum ConversationStep
{
    Idle,
    AwaitingBranchSelection,
    AwaitingServiceSelection,
    AwaitingSlotSelection,
    AwaitingPhoneNumber,
}
