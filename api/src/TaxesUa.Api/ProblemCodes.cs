namespace TaxesUa.Api;

/// <summary>
/// Every machine code the api puts on a failure (ADR-028): <c>code</c> on a ProblemDetails, and one per
/// field error in <c>errorCodes</c>. A code is a stable snake_case name; the web translates by it and
/// never reads <c>title</c>, <c>detail</c> or <c>errors</c>. Rename one only together with its web
/// texts: a test keeps this list and the web messages in step.
/// </summary>
internal static class ProblemCodes
{
    // Every failure.
    public const string ValidationFailed = "validation_failed";
    public const string CrossSiteRequest = "cross_site_request";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string PayloadTooLarge = "payload_too_large";

    // Sign-in.
    public const string GoogleNotConfigured = "google_not_configured";
    public const string ExternalSignInIncomplete = "external_sign_in_incomplete";
    public const string AccountNotAllowed = "account_not_allowed";
    public const string AdminRequired = "admin_required";
    public const string AccountCreateFailed = "account_create_failed";
    public const string GoogleLinkFailed = "google_link_failed";
    public const string PasskeyCredentialMissing = "passkey_credential_missing";
    public const string PasskeyRegistrationNotStarted = "passkey_registration_not_started";
    public const string PasskeyRegistrationFailed = "passkey_registration_failed";
    public const string PasskeySaveFailed = "passkey_save_failed";
    public const string PasskeySignInNotStarted = "passkey_sign_in_not_started";
    public const string PasskeyVerificationFailed = "passkey_verification_failed";
    public const string PasskeyUpdateFailed = "passkey_update_failed";

    // Something does not exist.
    public const string TaxYearNotFound = "tax_year_not_found";
    public const string PaymentNotFound = "payment_not_found";
    public const string CandidateNotFound = "candidate_not_found";
    public const string InvoiceNotFound = "invoice_not_found";
    public const string TransactionNotFound = "transaction_not_found";
    public const string ClientNotFound = "client_not_found";
    public const string QuarterBeforeRegistration = "quarter_before_registration";

    // Tax years, payments and Treasury accounts.
    public const string TaxYearAlreadyExists = "tax_year_already_exists";
    public const string TaxYearNotConfigured = "tax_year_not_configured";
    public const string PeriodNotPayable = "period_not_payable";
    public const string CandidateAlreadyDecided = "candidate_already_decided";
    public const string CandidateLinkChanged = "candidate_link_changed";
    public const string CandidateMatchRecorded = "candidate_match_recorded";
    public const string CandidateAlreadyConfirmed = "candidate_already_confirmed";
    public const string TreasuryNothingToRevert = "treasury_nothing_to_revert";
    public const string TreasuryNothingToEnd = "treasury_nothing_to_end";

    // Transactions and clients.
    public const string ReceiptHasRefunds = "receipt_has_refunds";
    public const string TransactionKindChanged = "transaction_kind_changed";
    public const string ClientHasReceipts = "client_has_receipts";
    public const string ClientHasInvoices = "client_has_invoices";

    // Invoices.
    public const string InvoiceIncomplete = "invoice_incomplete";
    public const string InvoiceCancelled = "invoice_cancelled";
    public const string InvoiceIsDraft = "invoice_is_draft";
    public const string InvoiceAlreadyPaid = "invoice_already_paid";
    public const string InvoiceNotEditable = "invoice_not_editable";
    public const string InvoiceNotDeletable = "invoice_not_deletable";
    public const string InvoiceHasReceipts = "invoice_has_receipts";
    public const string DraftHasNoNumber = "draft_has_no_number";
    public const string OnlyReceiptsPay = "only_receipts_pay";
    public const string ReceiptAlreadyLinked = "receipt_already_linked";
    public const string CurrencyMismatch = "currency_mismatch";

    // Declarations.
    public const string DeclarationNotReady = "declaration_not_ready";
    public const string QuarterNotEnded = "quarter_not_ended";
    public const string FileGeneratedBeforeQuarterEnd = "file_generated_before_quarter_end";
    public const string DeclarationSchemaInvalid = "declaration_schema_invalid";

    // Exchange rates.
    public const string FxRateNotPublished = "fx_rate_not_published";
    public const string FxServiceUnavailable = "fx_service_unavailable";

    // monobank.
    public const string MonobankNotConfigured = "monobank_not_configured";
    public const string MonobankNotConnected = "monobank_not_connected";
    public const string MonobankTokenRejected = "monobank_token_rejected";
    public const string MonobankTokenUnreadable = "monobank_token_unreadable";
    public const string MonobankRateLimited = "monobank_rate_limited";
    public const string MonobankUnavailable = "monobank_unavailable";
    public const string NoReserveJar = "no_reserve_jar";
    public const string ReserveJarNotOffered = "reserve_jar_not_offered";

    // Notifications.
    public const string TelegramNotConfigured = "telegram_not_configured";
    public const string TelegramUnavailable = "telegram_unavailable";
    public const string EmailNotConfigured = "email_not_configured";
    public const string EmailLinkInvalid = "email_link_invalid";
    public const string EmailLinkExpired = "email_link_expired";
    public const string NoAddressPending = "no_address_pending";
    public const string ChannelNotConnected = "channel_not_connected";
    public const string ChannelNotConfirmed = "channel_not_confirmed";
    public const string ChannelDeliveryFailed = "channel_delivery_failed";

    // Backup files.
    public const string BackupInvalid = "backup_invalid";
    public const string BackupNotABackup = "backup_not_a_backup";
    public const string BackupNotValid = "backup_not_valid";
    public const string BackupVersionUnsupported = "backup_version_unsupported";
    public const string BackupMissingInvoices = "backup_missing_invoices";
    public const string SignatureEmpty = "signature_empty";
    public const string SignatureTooLarge = "signature_too_large";
    public const string SignatureTypeUnsupported = "signature_type_unsupported";
    public const string SignatureNotAnImage = "signature_not_an_image";

    // Field errors: a value.
    public const string Required = "required";
    public const string TooLong = "too_long";
    public const string ControlCharacter = "control_character";
    public const string InvalidValue = "invalid_value";
    public const string OutOfRange = "out_of_range";
    public const string NotPositive = "not_positive";
    public const string AmountTooLarge = "amount_too_large";
    public const string YearOutOfRange = "year_out_of_range";
    public const string DateInFuture = "date_in_future";
    public const string NotAllowed = "not_allowed";
    public const string NullItem = "null_item";
    public const string DuplicateValue = "duplicate_value";
    public const string UnknownReference = "unknown_reference";

    // Field errors: a particular field.
    public const string RnokppInvalid = "rnokpp_invalid";
    public const string SwiftInvalid = "swift_invalid";
    public const string RecipientCodeInvalid = "recipient_code_invalid";
    public const string EmailInvalid = "email_invalid";
    public const string PhoneInvalid = "phone_invalid";
    public const string CountryInvalid = "country_invalid";
    public const string NameTaken = "name_taken";
    public const string CurrencyDuplicate = "currency_duplicate";
    public const string IbanLength = "iban_length";
    public const string IbanPrefix = "iban_prefix";
    public const string IbanCharacters = "iban_characters";
    public const string IbanBankId = "iban_bank_id";
    public const string IbanChecksum = "iban_checksum";
    public const string TaxOfficeRegionRange = "tax_office_region_range";
    public const string TaxOfficeDistrictRange = "tax_office_district_range";
    public const string TaxOfficePairIncomplete = "tax_office_pair_incomplete";
    public const string KvedFormatInvalid = "kved_format_invalid";
    public const string KvedDuplicate = "kved_duplicate";
    public const string KvedUnknown = "kved_unknown";
    public const string KvedTooMany = "kved_too_many";
    public const string TokenRejected = "token_rejected";
    public const string UnknownJar = "unknown_jar";
    public const string UnknownClient = "unknown_client";
    public const string UnknownReceipt = "unknown_receipt";

    // Field errors: dates and periods.
    public const string DateBeforeRegistration = "date_before_registration";
    public const string RegistrationDateRequired = "registration_date_required";
    public const string RegistrationDateAfterGroup3Receipt = "registration_date_after_group3_receipt";
    public const string QuarterStartRequired = "quarter_start_required";
    public const string InvalidQuarter = "invalid_quarter";
    public const string FiledBeforeQuarterEnd = "filed_before_quarter_end";
    public const string DueDateBeforeIssueDate = "due_date_before_issue_date";
    public const string PeriodAmbiguous = "period_ambiguous";
    public const string QuarterOutOfRange = "quarter_out_of_range";
    public const string MonthOutOfRange = "month_out_of_range";
    public const string WeekendAllDays = "weekend_all_days";

    // Field errors: invoices and receipts.
    public const string TooManyLines = "too_many_lines";
    public const string NoLines = "no_lines";
    public const string QuantityOutOfRange = "quantity_out_of_range";
    public const string RateOutOfRange = "rate_out_of_range";
    public const string TotalNotPositive = "total_not_positive";
    public const string DetailMissing = "detail_missing";
    public const string PaymentDetailsMissing = "payment_details_missing";
    public const string RefundCurrencyMismatch = "refund_currency_mismatch";
    public const string RefundExceedsReceipt = "refund_exceeds_receipt";
    public const string RefundLinkNotAllowed = "refund_link_not_allowed";
    public const string KindLockedByRefunds = "kind_locked_by_refunds";
    public const string CurrencyLockedByRefunds = "currency_locked_by_refunds";
    public const string AmountBelowLinkedRefunds = "amount_below_linked_refunds";
    public const string KindLockedByInvoice = "kind_locked_by_invoice";
    public const string CurrencyLockedByInvoice = "currency_locked_by_invoice";
    public const string InvoiceNumberLocked = "invoice_number_locked";
    public const string ManualRateNotAllowed = "manual_rate_not_allowed";

    // Field errors: a backup file.
    public const string IdNotUnique = "id_not_unique";
    public const string InconsistentFields = "inconsistent_fields";
}
