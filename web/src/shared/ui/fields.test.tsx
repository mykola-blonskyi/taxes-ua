import { useTranslations } from "next-intl";
import { describe, expect, it } from "vitest";
import { renderApp, screen } from "@/test/harness";
import {
  FieldErrors,
  FieldForm,
  FieldWrapper,
  MoneyField,
  NumberField,
  RateField,
  SelectField,
  TextAreaField,
  TextField,
} from "./fields";

const noop = () => {};

// Both languages, because the message is read aloud in the owner's language and the id wiring must not
// depend on the text.
const locales = [
  { locale: "uk", message: "Введіть суму більше нуля, наприклад 1234,56." },
  { locale: "ru", message: "Введите сумму больше нуля, например 1234,56." },
] as const;

function Fields({ errors, hint }: { errors?: string[]; hint?: string }) {
  const t = useTranslations("pay");

  return (
    <form>
      <TextField id="text" label={t("amountInput")} value="" onChange={noop} errors={errors} hint={hint} />
      <TextAreaField id="area" label="Area" value="" onChange={noop} errors={errors} hint={hint} />
      <NumberField id="number" label="Number" value={0} onChange={noop} errors={errors} hint={hint} />
      <SelectField
        id="select"
        label="Select"
        value="a"
        onChange={noop}
        options={[{ value: "a", label: "A" }]}
        errors={errors}
        hint={hint}
      />
      <MoneyField id="money" label="Money" locale="uk" valueKop={0} onChange={noop} errors={errors} hint={hint} />
      <RateField id="rate" label="Rate" locale="uk" valueBp={0} onChange={noop} errors={errors} hint={hint} />
    </form>
  );
}

const controls = ["text", "area", "number", "select", "money", "rate"];

describe.each(locales)("a field in $locale", ({ locale, message }) => {
  it("is not marked invalid and announces nothing while it has no error", () => {
    renderApp(<Fields />, { locale });

    for (const id of controls) {
      const control = document.getElementById(id)!;
      expect(control).not.toHaveAttribute("aria-invalid");
      expect(control).not.toHaveAttribute("aria-describedby");
    }
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("is invalid, described by its error text and announced when it has an error", () => {
    renderApp(<Fields errors={[message]} />, { locale });

    for (const id of controls) {
      const control = document.getElementById(id)!;
      expect(control).toHaveAttribute("aria-invalid", "true");
      expect(control).toHaveAttribute("aria-describedby", `${id}-error`);
      expect(control).toHaveAccessibleDescription(message);
    }
    const alerts = screen.getAllByRole("alert");
    expect(alerts).toHaveLength(controls.length);
    expect(alerts[0]).toHaveTextContent(message);
  });

  it("is described by its hint first and its error second", () => {
    renderApp(<Fields errors={[message]} hint="Підказка" />, { locale });

    const control = document.getElementById("text")!;
    expect(control).toHaveAttribute("aria-describedby", "text-hint text-error");
    expect(control).toHaveAccessibleDescription(`Підказка ${message}`);
  });

  it("points at the hint alone when there is a hint and no error", () => {
    renderApp(<Fields hint="Підказка" />, { locale });

    const control = document.getElementById("text")!;
    expect(control).toHaveAttribute("aria-describedby", "text-hint");
    expect(control).not.toHaveAttribute("aria-invalid");
  });
});

describe("FieldWrapper with its own control", () => {
  it("hands the control the id and the aria wiring", () => {
    renderApp(
      <FieldWrapper label="Period" htmlFor="period" errors={["Wrong"]}>
        {(control) => <select {...control} />}
      </FieldWrapper>,
    );

    const select = screen.getByLabelText("Period");
    expect(select).toHaveAttribute("aria-invalid", "true");
    expect(select).toHaveAccessibleDescription("Wrong");
  });

  it("treats an empty list of errors as no error", () => {
    renderApp(<TextField id="empty" label="Empty" value="" onChange={noop} errors={[]} />);

    expect(screen.getByLabelText("Empty")).not.toHaveAttribute("aria-invalid");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("FieldErrors", () => {
  it("renders an alert with every message, and nothing without one", () => {
    const { rerender } = renderApp(<FieldErrors id="group-error" errors={["One", "Two"]} />);

    expect(screen.getByRole("alert")).toHaveAttribute("id", "group-error");
    expect(screen.getByRole("alert")).toHaveTextContent("OneTwo");

    rerender(<FieldErrors id="group-error" errors={undefined} />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("a form whose summary is announcing", () => {
  it("keeps each field message in the field's description but not live", () => {
    renderApp(
      <FieldForm quietErrors>
        <p role="alert">Summary</p>
        <TextField id="a" label="A" value="" onChange={noop} errors={["Wrong A"]} />
        <TextField id="b" label="B" value="" onChange={noop} errors={["Wrong B"]} />
      </FieldForm>,
    );

    expect(screen.getAllByRole("alert")).toHaveLength(1);
    expect(screen.getByLabelText("A")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("A")).toHaveAccessibleDescription("Wrong A");
    expect(screen.getByLabelText("B")).toHaveAccessibleDescription("Wrong B");
  });
});
