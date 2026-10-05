using System.Text.Json.Nodes;
using TaxesUa.Api.Features.Transactions;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;

namespace TaxesUa.Api.Features.Backup;

internal sealed partial record BackupDocument
{
    public static void Upgrade(JsonObject root, int version)
    {
        if (version == 1)
        {
            UpgradeFromVersion1(root);
        }

        if (version <= 2)
        {
            UpgradeFromVersion2(root);
        }

        if (version <= 3)
        {
            UpgradeFromVersion3(root);
        }

        if (version <= 4)
        {
            UpgradeFromVersion4(root);
        }

        if (version <= 5)
        {
            UpgradeFromVersion5(root);
        }

        if (version <= 6)
        {
            UpgradeFromVersion6(root);
        }

        if (version <= 7)
        {
            UpgradeFromVersion7(root);
        }

        if (version <= 8)
        {
            UpgradeFromVersion8(root);
        }

        if (version <= 9)
        {
            UpgradeFromVersion9(root);
        }

        if (version <= 10)
        {
            UpgradeFromVersion10(root);
        }

        if (version <= 11)
        {
            UpgradeFromVersion11(root);
        }

        if (version <= 12)
        {
            UpgradeFromVersion12(root);
        }

        if (version <= 13)
        {
            UpgradeFromVersion13(root);
        }

        if (version <= 14)
        {
            UpgradeFromVersion14(root);
        }

        if (version <= 15)
        {
            UpgradeFromVersion15(root);
        }

        if (version <= 16)
        {
            UpgradeFromVersion16(root);
        }

        if (version <= 17)
        {
            UpgradeFromVersion17(root);
        }

        if (version <= 18)
        {
            UpgradeFromVersion18(root);
        }
    }

    // A version 3 file predates the invoicing details.
    private static void UpgradeFromVersion3(JsonObject root)
    {
        root["schemaVersion"] = 4;
        root["invoicingDetails"] = null;
    }

    // A version 1 file predates bank imports: no accounts, no batches, and every row the owner's own.
    private static void UpgradeFromVersion1(JsonObject root)
    {
        root["bankAccounts"] = new JsonArray();
        root["importBatches"] = new JsonArray();
        if (root["transactions"] is not JsonArray transactions)
        {
            return;
        }

        foreach (var transaction in transactions.OfType<JsonObject>())
        {
            transaction["bankAccountId"] = null;
            transaction["externalId"] = null;
            transaction["bankTime"] = null;
            transaction["counterparty"] = null;
            transaction["importBatchId"] = null;
            transaction["reviewStatus"] = nameof(ReviewStatus.Confirmed);
        }
    }

    // A version 2 file predates budget payment candidates: every payment was typed by the owner.
    private static void UpgradeFromVersion2(JsonObject root)
    {
        root["schemaVersion"] = 3;
        root["budgetPaymentCandidates"] = new JsonArray();
        if (root["budgetPayments"] is not JsonArray payments)
        {
            return;
        }

        foreach (var payment in payments.OfType<JsonObject>())
        {
            payment["bankAccountId"] = null;
            payment["externalId"] = null;
        }
    }

    // A version 4 file predates client details: every client has only its name.
    private static void UpgradeFromVersion4(JsonObject root)
    {
        root["schemaVersion"] = 5;
        if (root["clients"] is not JsonArray clients)
        {
            return;
        }

        foreach (var client in clients.OfType<JsonObject>())
        {
            client["address"] = null;
            client["country"] = null;
            client["vatId"] = null;
            client["email"] = null;
            client["defaultCurrency"] = null;
            client["notes"] = null;
        }
    }

    // A version 5 file predates invoices.
    private static void UpgradeFromVersion5(JsonObject root)
    {
        root["schemaVersion"] = 6;
        root["invoices"] = new JsonArray();
    }

    // A version 6 file predates the declaration: no details and nothing marked filed.
    private static void UpgradeFromVersion6(JsonObject root)
    {
        root["schemaVersion"] = 7;
        root["declarationDetails"] = null;
        root["declarationFilings"] = new JsonArray();
    }

    // A version 7 file predates paying an invoice with a receipt: no receipt is linked.
    private static void UpgradeFromVersion7(JsonObject root)
    {
        root["schemaVersion"] = 8;
        if (root["transactions"] is not JsonArray transactions)
        {
            return;
        }

        foreach (var transaction in transactions.OfType<JsonObject>())
        {
            transaction["invoiceId"] = null;
        }
    }

    // A version 8 file predates Treasury accounts, and its candidates never kept the counterparty's code.
    private static void UpgradeFromVersion8(JsonObject root)
    {
        root["schemaVersion"] = 9;
        root["treasuryAccounts"] = new JsonArray();
        if (root["budgetPaymentCandidates"] is not JsonArray candidates)
        {
            return;
        }

        foreach (var candidate in candidates.OfType<JsonObject>())
        {
            candidate["counterEdrpou"] = null;
        }
    }

    // A version 9 file predates the return to group 3 after a limit crossing: none is set.
    private static void UpgradeFromVersion9(JsonObject root)
    {
        root["schemaVersion"] = 10;
        if (root["settings"] is JsonObject settings)
        {
            settings["backOnGroup3From"] = null;
        }
    }

    // A version 10 file predates the notification channels: none is connected.
    private static void UpgradeFromVersion10(JsonObject root)
    {
        root["schemaVersion"] = 11;
        root["notificationChannels"] = new JsonArray();
    }

    // A version 11 file predates the declaration file and the tax office's name.
    private static void UpgradeFromVersion11(JsonObject root)
    {
        root["schemaVersion"] = 12;
        root["declarationFiles"] = new JsonArray();
        if (root["declarationDetails"] is JsonObject details)
        {
            details["taxOfficeName"] = string.Empty;
        }
    }

    // A version 12 file predates the ESV annex: no declaration file had one.
    private static void UpgradeFromVersion12(JsonObject root)
    {
        root["schemaVersion"] = 13;
        if (root["declarationFiles"] is JsonArray files)
        {
            foreach (var file in files.OfType<JsonObject>())
            {
                file["annexFileName"] = null;
                file["annexContent"] = null;
            }
        }
    }

    // A version 13 file predates the reserve jar: none was chosen.
    private static void UpgradeFromVersion13(JsonObject root)
    {
        root["schemaVersion"] = 14;
        root["reserveJar"] = null;
    }

    // A version 14 file predates email and the confirmation of a channel (#107). Every channel it holds is
    // a Telegram chat, confirmed when it was linked.
    private static void UpgradeFromVersion14(JsonObject root)
    {
        root["schemaVersion"] = 15;
        if (root["notificationChannels"] is JsonArray channels)
        {
            foreach (var channel in channels.OfType<JsonObject>())
            {
                channel["confirmedAt"] = channel["linkedAt"]?.DeepClone();
            }
        }
    }

    // A version 15 file predates the DPS status (#172): the app assumed group 3 from registration, which a
    // null group3Since says, and nothing was confirmed or ticked, as the migration leaves a stored owner. It also
    // predates the move off the wrong Prorated default, and cannot tell that default from a deliberate
    // choice, so its Prorated is read as FullMonth, as the migration did for stored owners (ADR-018
    // amendment). A version 16 file is written after that move, so its Prorated is the owner's choice.
    private static void UpgradeFromVersion15(JsonObject root)
    {
        root["schemaVersion"] = 16;
        if (root["settings"] is JsonObject settings)
        {
            if (settings["esvRegistrationMonthPolicy"]?.ToString() == nameof(EsvRegistrationMonthPolicy.Prorated))
            {
                settings["esvRegistrationMonthPolicy"] = nameof(EsvRegistrationMonthPolicy.FullMonth);
            }

            settings["group3Since"] = null;
            settings["group3Confirmation"] = null;
            settings["dpsFopRegistered"] = false;
            settings["dpsEsvRegistered"] = false;
            settings["dpsAccountsRegistered"] = false;
        }
    }

    // A version 16 file predates the end of a Treasury account: none had one.
    private static void UpgradeFromVersion16(JsonObject root)
    {
        root["schemaVersion"] = 17;
        if (root["treasuryAccounts"] is JsonArray accounts)
        {
            foreach (var account in accounts.OfType<JsonObject>())
            {
                account["manualValidUntil"] = null;
                account["learnedValidUntil"] = null;
            }
        }
    }

    // A version 17 file predates the full name, phone and email the declaration's header prints: none was set.
    private static void UpgradeFromVersion17(JsonObject root)
    {
        root["schemaVersion"] = 18;
        if (root["declarationDetails"] is JsonObject details)
        {
            details["fullName"] = string.Empty;
            details["phone"] = string.Empty;
            details["reportEmail"] = string.Empty;
        }
    }

    // A version 18 file predates removing an end: an account without one had nothing said about it.
    private static void UpgradeFromVersion18(JsonObject root)
    {
        root["schemaVersion"] = CurrentSchemaVersion;
        if (root["treasuryAccounts"] is JsonArray accounts)
        {
            foreach (var account in accounts.OfType<JsonObject>())
            {
                account["manualEndRemoved"] = false;
                account["learnedEndRemoved"] = false;
            }
        }
    }
}
