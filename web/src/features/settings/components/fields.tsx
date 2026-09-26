import type { ComponentProps, ReactNode } from "react";
import { cn } from "@/shared/lib/utils";

const inputClasses =
  "w-full min-w-24 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50";

export function formatMoney(kopecks: number, locale: string) {
  const formatted = new Intl.NumberFormat(locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(kopecks / 100);

  return `${formatted} ₴`;
}

export function formatRate(basisPoints: number, locale: string) {
  return new Intl.NumberFormat(locale, {
    style: "percent",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(basisPoints / 10000);
}

function numberOrZero(value: number): number {
  return Number.isNaN(value) ? 0 : value;
}

export function FieldWrapper({
  label,
  htmlFor,
  hint,
  errors,
  labelClassName,
  children,
}: {
  label: string;
  htmlFor: string;
  hint?: string;
  errors?: string[];
  labelClassName?: string;
  children: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={htmlFor} className={labelClassName ?? "text-sm font-medium"}>
        {label}
      </label>
      {children}
      {hint ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
      {errors && errors.length > 0 ? (
        <ul className="text-xs text-destructive">
          {errors.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

type BaseFieldProps = {
  id: string;
  label: string;
  hint?: string;
  errors?: string[];
  labelClassName?: string;
};

export function TextField({
  id,
  label,
  hint,
  errors,
  labelClassName,
  value,
  onChange,
  ...inputProps
}: BaseFieldProps & {
  value: string;
  onChange: (value: string) => void;
} & Omit<ComponentProps<"input">, "id" | "value" | "onChange">) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      <input
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={inputClasses}
        {...inputProps}
      />
    </FieldWrapper>
  );
}

export function NumberField({
  id,
  label,
  hint,
  errors,
  labelClassName,
  value,
  onChange,
  ...inputProps
}: BaseFieldProps & {
  value: number;
  onChange: (value: number) => void;
} & Omit<ComponentProps<"input">, "id" | "value" | "onChange" | "type">) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      <input
        id={id}
        type="number"
        step={1}
        value={value}
        onChange={(event) => onChange(numberOrZero(event.target.valueAsNumber))}
        className={inputClasses}
        {...inputProps}
      />
    </FieldWrapper>
  );
}

export function CheckboxField({
  id,
  label,
  checked,
  onChange,
  labelClassName,
}: {
  id: string;
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  labelClassName?: string;
}) {
  return (
    <div className="flex items-center gap-2">
      <input
        id={id}
        type="checkbox"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
        className="size-4 rounded border bg-background"
      />
      <label htmlFor={id} className={labelClassName ?? "text-sm"}>
        {label}
      </label>
    </div>
  );
}

export function SelectField({
  id,
  label,
  hint,
  errors,
  labelClassName,
  value,
  onChange,
  options,
}: BaseFieldProps & {
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
}) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={inputClasses}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldWrapper>
  );
}

export function MoneyField({
  id,
  label,
  hint,
  errors,
  labelClassName,
  locale,
  valueKop,
  onChange,
}: BaseFieldProps & {
  locale: string;
  valueKop: number;
  onChange: (valueKop: number) => void;
}) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      <input
        id={id}
        type="number"
        step={1}
        value={valueKop}
        onChange={(event) => onChange(numberOrZero(event.target.valueAsNumber))}
        className={inputClasses}
      />
      <span className="text-xs text-muted-foreground">{formatMoney(valueKop, locale)}</span>
    </FieldWrapper>
  );
}

export function RateField({
  id,
  label,
  hint,
  errors,
  labelClassName,
  locale,
  valueBp,
  onChange,
}: BaseFieldProps & {
  locale: string;
  valueBp: number;
  onChange: (valueBp: number) => void;
}) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      <input
        id={id}
        type="number"
        step={1}
        value={valueBp}
        onChange={(event) => onChange(numberOrZero(event.target.valueAsNumber))}
        className={inputClasses}
      />
      <span className="text-xs text-muted-foreground">{formatRate(valueBp, locale)}</span>
    </FieldWrapper>
  );
}

export function ReadOnlyMoneyField({
  id,
  label,
  labelClassName,
  locale,
  valueKop,
}: {
  id: string;
  label: string;
  labelClassName?: string;
  locale: string;
  valueKop: number;
}) {
  return (
    <FieldWrapper label={label} htmlFor={id} labelClassName={labelClassName}>
      <input
        id={id}
        type="text"
        value={formatMoney(valueKop, locale)}
        disabled
        className={cn(inputClasses, "text-muted-foreground")}
      />
    </FieldWrapper>
  );
}
