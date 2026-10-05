import { describe, expect, it } from "vitest";
import ru from "../../../messages/ru.json";
import uk from "../../../messages/uk.json";
import { auditedEntities, auditFieldKeys } from "./fields";

const catalogs = { uk: uk.audit, ru: ru.audit };

// The same entity list gates the history page's ?entity= filter and the entity labels, so a new audited
// entity or field that the catalogs do not know shows a raw key instead of a word.
describe.each(Object.entries(catalogs))("audit catalog in %s", (_locale, audit) => {
  it("labels every audited entity", () => {
    for (const entity of auditedEntities) {
      expect(audit.entities, entity).toHaveProperty(entity);
    }
  });

  it("labels every audited field", () => {
    const missing = auditedEntities.flatMap((entity) =>
      auditFieldKeys(entity)
        .filter((key) => !(key in audit.fields))
        .map((key) => `${entity}.${key}`),
    );

    expect(missing).toEqual([]);
  });
});

describe("auditedEntities", () => {
  it.each(["Backup", "TreasuryAccount", "NotificationChannel"])("includes %s", (entity) => {
    expect(auditedEntities).toContain(entity);
  });
});
