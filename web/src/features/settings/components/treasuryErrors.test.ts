import { describe, expect, it } from "vitest";
import { errorKey } from "./treasuryErrors";

describe("errorKey for a Treasury account", () => {
  it.each([
    ["the IBAN is not a Treasury account", "iban", "iban must be a Treasury account: a valid Ukrainian IBAN with bank id 899998.", "iban"],
    ["the recipient name is rejected", "recipientName", "recipientName is required.", "recipientName"],
    ["the recipient code is rejected", "recipientCode", "recipientCode must be 8 digits.", "recipientCode"],
    ["a field is too long", "recipientName", "recipientName must not exceed 140 characters.", "tooLong"],
    ["a field holds a control character", "recipientName", "recipientName must not contain a control character.", "controlChar"],
    ["a too-long IBAN is a length problem, not an IBAN problem", "iban", "iban must not exceed 34 characters.", "tooLong"],
    ["a control character in the IBAN is not an IBAN problem", "iban", "iban must not contain a control character.", "controlChar"],
  ])("maps %s", (_name, field, message, key) => {
    expect(errorKey(field, message)).toBe(key);
  });

  it.each([
    ["an unknown field", "kind", "kind is unknown."],
    ["an empty field name", "", "something is wrong."],
  ])("leaves %s as the api worded it", (_name, field, message) => {
    expect(errorKey(field, message)).toBeNull();
  });
});
