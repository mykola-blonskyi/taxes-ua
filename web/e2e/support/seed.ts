import { randomUUID } from "node:crypto";
import { expect, type APIRequestContext } from "@playwright/test";
import { json, seedRegisteredOwner } from "./api";

export type PaymentKind = "SingleTax" | "MilitaryLevy" | "Esv";

// The two check digits that make the 25-character BBAN a valid Ukrainian IBAN (ISO 13616: "UA00" moved
// to the end, letters read as 10 to 35, 98 minus the remainder modulo 97).
export function ukrainianIban(bban: string) {
  const check = BigInt(98) - (BigInt(`${bban}301000`) % BigInt(97));
  return `UA${String(check).padStart(2, "0")}${bban}`;
}

const treasuryBankId = "899998";

function treasuryAccount(index: number, recipientName: string, recipientCode: string) {
  return { iban: ukrainianIban(`${treasuryBankId}${String(index).padStart(19, "0")}`), recipientName, recipientCode };
}

// One distinct account per kind, so a screen that mixed two kinds up would show the wrong one.
export const treasuryAccounts: Record<PaymentKind, { iban: string; recipientName: string; recipientCode: string }> = {
  SingleTax: treasuryAccount(1, "Тестове казначейство, єдиний податок", "37000001"),
  MilitaryLevy: treasuryAccount(2, "Тестове казначейство, військовий збір", "37000002"),
  Esv: treasuryAccount(3, "Тестове казначейство, ЄСВ", "37000003"),
};

export async function seedTreasuryAccounts(owner: APIRequestContext) {
  for (const [kind, details] of Object.entries(treasuryAccounts)) {
    const saved = await owner.put(`/api/settings/treasury-accounts/${kind}`, { data: details });
    expect(saved.ok(), `treasury account ${kind} answered ${saved.status()}`).toBe(true);
  }
}

export const seller = { rnokpp: "1234567890", nameEn: "FOP Testenko Test", nameUk: "ФОП Тестенко Тест Тестович" };

// Fills the invoicing details an invoice needs to be issued and a declaration needs for its header. The
// values are fixed, so every test that calls it writes the same ones and the order does not matter.
export async function seedInvoicingDetails(owner: APIRequestContext) {
  const current = await json<{ defaults: Record<string, string> }>(await owner.get("/api/settings/invoicing"));
  const saved = await owner.put("/api/settings/invoicing", {
    data: {
      sellerNameUk: seller.nameUk,
      sellerNameEn: seller.nameEn,
      rnokpp: seller.rnokpp,
      addressUk: "м. Київ, вул. Тестова, 1",
      addressEn: "1 Test St, Kyiv",
      ...current.defaults,
      paymentDetails: [
        {
          currency: "UAH",
          iban: ukrainianIban("3223130000026007233566001"),
          beneficiaryBank: "JSC Universal Bank, Kyiv",
          swift: "UNJSUAUKXXX",
          intermediaryBank: "",
          intermediarySwift: "",
          intermediaryAccount: "",
        },
      ],
    },
  });
  expect(saved.ok(), `invoicing details answered ${saved.status()}`).toBe(true);
}

export const taxOffice = { region: 26, district: 1, name: "Тестова ДПС", address: "м. Київ, вул. Тестова, 1" };

// Brings the owner to the point where the declaration for any quarter of `targetYear` (the current year by
// default) is ready: a verified tax year, the invoicing identity and the declaration's tax office,
// address and KVED code.
export async function seedDeclarationReady(owner: APIRequestContext, targetYear?: number) {
  const { year } = await seedRegisteredOwner(owner, targetYear);
  expect((await owner.post(`/api/tax-years/${year}/verify`)).ok()).toBe(true);
  await seedInvoicingDetails(owner);
  const saved = await owner.put("/api/settings/declaration", {
    data: {
      taxOfficeRegion: taxOffice.region,
      taxOfficeDistrict: taxOffice.district,
      taxOfficeName: taxOffice.name,
      kvedCodes: ["62.01"],
      address: taxOffice.address,
    },
  });
  expect(saved.ok(), `declaration details answered ${saved.status()}`).toBe(true);
  return { year };
}

type Invoice = { id: string; number: string; pdfFileName: string };

// A client with a name of its own and an issued one-line invoice for it, in hryvnias only: an invoice in
// a foreign currency would need an NBU rate, and the suite never calls the real NBU.
export async function seedIssuedInvoice(owner: APIRequestContext, issueDate: string) {
  await seedInvoicingDetails(owner);
  const clientName = `E2E Client ${randomUUID().slice(0, 8)}`;
  const client = await json<{ id: string }>(
    await owner.post("/api/clients", {
      data: { name: clientName, address: "1 Test Street, Berlin", country: "DE", vatId: null, email: null, defaultCurrency: null, notes: null },
    }),
  );
  const draft = await json<Invoice>(
    await owner.post("/api/invoices", {
      data: {
        clientId: client.id,
        issueDate,
        dueDate: issueDate,
        currency: "UAH",
        lines: [
          { descriptionEn: "Consulting", descriptionUk: "Консультація", unit: "Hour", quantityThousandths: 2000, rateMinor: 150000 },
        ],
      },
    }),
  );
  const invoice = await json<Invoice>(await owner.post(`/api/invoices/${draft.id}/issue`));
  return { clientName, invoice };
}
