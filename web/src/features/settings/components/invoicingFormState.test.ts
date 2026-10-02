import { describe, expect, it } from "vitest";
import { errorKey } from "./invoicingFormState";

describe("errorKey for the invoicing details", () => {
  it.each([
    ["an invalid IBAN", "iban", "iban must be a valid Ukrainian IBAN: UA and 27 characters.", "iban"],
    ["an invalid tax number", "rnokpp", "rnokpp must be 10 digits.", "rnokpp"],
    ["an invalid SWIFT code", "swift", "swift must be 8 or 11 characters.", "swift"],
    ["a missing beneficiary bank", "beneficiaryBank", "beneficiaryBank is required.", "beneficiaryBank"],
    ["any other missing field", "sellerNameEn", "sellerNameEn is required.", "required"],
    ["a field that is too long", "addressUk", "addressUk must not exceed 500 characters.", "tooLong"],
    ["a field with a control character", "addressUk", "addressUk must not contain a control character.", "controlChar"],
    ["a repeated currency", "currency", "currency must not repeat: one set of payment details per currency.", "duplicateCurrency"],
    ["a too-long IBAN is a length problem", "iban", "iban must not exceed 34 characters.", "tooLong"],
  ])("maps %s", (_name, field, message, key) => {
    expect(errorKey(field, message)).toBe(key);
  });

  it.each([
    ["an unknown field with an unknown message", "intermediaryBank", "intermediaryBank is odd."],
    ["an empty message", "name", ""],
  ])("leaves %s as the api worded it", (_name, field, message) => {
    expect(errorKey(field, message)).toBeNull();
  });
});
