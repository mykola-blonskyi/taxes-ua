import { describe, expect, it } from "vitest";
import { renderApp, screen } from "@/test/harness";
import { ApiError } from "./client";
import { useApiErrorText } from "./useApiErrorText";

function Probe({ error, prefix = "Не вдалося зберегти:" }: { error: unknown; prefix?: string }) {
  const text = useApiErrorText();

  return (
    <dl>
      <dt>describe</dt>
      <dd data-testid="describe">{text.describe(error)}</dd>
      <dt>withReason</dt>
      <dd data-testid="withReason">{text.withReason(prefix, error)}</dd>
      <dt>fields</dt>
      <dd data-testid="fields">{JSON.stringify(error instanceof ApiError ? text.fieldTexts(error) : {})}</dd>
    </dl>
  );
}

const generic = "Не вдалося виконати дію. Спробуйте ще раз.";

describe("useApiErrorText", () => {
  it("words a known code in the owner's language", () => {
    renderApp(<Probe error={new ApiError(409, { code: "monobank_token_rejected", message: "English." })} />);

    expect(screen.getByTestId("describe")).toHaveTextContent("monobank відхилив токен. Підключіть його знову.");
    expect(screen.getByTestId("withReason")).toHaveTextContent(
      "Не вдалося зберегти: monobank відхилив токен. Підключіть його знову.",
    );
  });

  it("words it in Russian too", () => {
    renderApp(<Probe error={new ApiError(409, { code: "monobank_token_rejected" })} />, { locale: "ru" });

    expect(screen.getByTestId("describe")).toHaveTextContent("monobank отклонил токен. Подключите его снова.");
  });

  it.each([
    ["a code this build has no words for", new ApiError(500, { code: "a_code_from_the_future", message: "English." })],
    ["a failure with no code", new ApiError(502, { message: "Bad Gateway" })],
  ])("reads %s as the generic sentence, never the English", (_name, error) => {
    renderApp(<Probe error={error} />);

    expect(screen.getByTestId("describe")).toHaveTextContent(generic);
    expect(screen.getByTestId("describe")).not.toHaveTextContent(/English|Bad Gateway|Failed to fetch|boom/);
  });

  it("reads a dropped connection as the network sentence, never the English", () => {
    renderApp(<Probe error={new TypeError("Failed to fetch")} />);

    const network = "Немає зв'язку з сервером. Перевірте мережу й спробуйте ще раз.";

    expect(screen.getByTestId("describe")).toHaveTextContent(network);
    expect(screen.getByTestId("withReason")).toHaveTextContent(`Не вдалося зберегти: ${network}`);
    expect(screen.getByTestId("describe")).not.toHaveTextContent(/Failed to fetch/);
  });

  it.each([
    ["a plain Error", new Error("boom")],
    ["a WebAuthn InvalidStateError", new DOMException("The authenticator was used before.", "InvalidStateError")],
    ["a WebAuthn SecurityError", new DOMException("The origin is not allowed.", "SecurityError")],
    ["a thrown string", "boom"],
  ])("reads %s as the unexpected sentence, not as a network failure", (_name, error) => {
    renderApp(<Probe error={error} />);

    const unexpected = "Щось пішло не так. Спробуйте ще раз.";

    expect(screen.getByTestId("describe")).toHaveTextContent(unexpected);
    expect(screen.getByTestId("withReason")).toHaveTextContent(`Не вдалося зберегти: ${unexpected}`);
    expect(screen.getByTestId("describe")).not.toHaveTextContent(/boom|Немає зв'язку|authenticator|origin/);
  });

  it("follows the screen's own colon with the unexpected sentence when the api gave no reason it can word", () => {
    renderApp(<Probe error={new ApiError(502, { message: "Bad Gateway" })} />);

    expect(screen.getByTestId("withReason")).toHaveTextContent(
      "Не вдалося зберегти: Щось пішло не так. Спробуйте ще раз.",
    );
  });

  it("leaves a screen's finished sentence alone when the api gave no reason it can word", () => {
    renderApp(<Probe prefix="Не вдалося завантажити дані." error={new ApiError(502, { message: "Bad Gateway" })} />);

    expect(screen.getByTestId("withReason")).toHaveTextContent(/^Не вдалося завантажити дані\.$/);
  });

  it("makes a colon a full stop when there is no error to read", () => {
    renderApp(<Probe error={undefined} />);

    expect(screen.getByTestId("withReason")).toHaveTextContent(/^Не вдалося зберегти\.$/);
  });

  it("words each rejected field of a validation problem", () => {
    const error = new ApiError(400, {
      code: "validation_failed",
      fieldCodes: { iban: ["iban_checksum"], "lines[0].descriptionEn": ["too_long", "no_such_code"] },
    });

    renderApp(<Probe error={error} />);

    expect(JSON.parse(screen.getByTestId("fields").textContent!)).toEqual({
      iban: ["Контрольна сума IBAN не збігається: перевірте, чи немає помилки в цифрах."],
      "lines[0].descriptionEn": ["Текст задовгий.", generic],
    });
  });
});
