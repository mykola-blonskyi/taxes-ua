"use client";

import { createContext, useContext, type ComponentProps, type ReactNode } from "react";
import { formatMoney, formatRate } from "@/shared/lib/money";
import { cn } from "@/shared/lib/utils";

const inputClasses =
  "w-full min-w-24 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring aria-invalid:border-destructive pointer-coarse:min-h-11";

function numberOrZero(value: number): number {
  return Number.isNaN(value) ? 0 : value;
}

// What a control must carry to be tied to its label's hint and error: the id the label points at, a flag
// when the field is wrong, and the ids of the text that explains it. FieldWrapper hands these to its child.
export type ControlProps = {
  id: string;
  "aria-invalid"?: true;
  "aria-describedby"?: string;
};

// A form that rejects a submit shows one summary that is a live region. While it does, the messages under
// each field stay reachable through aria-describedby but are not live, so a screen reader hears the summary
// once and not once per field.
const QuietFieldErrors = createContext(false);

export function QuietFieldErrorsScope({ quiet, children }: { quiet: boolean; children: ReactNode }) {
  return <QuietFieldErrors value={quiet}>{children}</QuietFieldErrors>;
}

// A <form> whose field messages go quiet while `quietErrors` is true (its summary is announcing instead).
export function FieldForm({ quietErrors, ...props }: ComponentProps<"form"> & { quietErrors: boolean }) {
  return (
    <QuietFieldErrorsScope quiet={quietErrors}>
      <form {...props} />
    </QuietFieldErrorsScope>
  );
}

// The messages under a field. A role="alert" region that appears with its text is announced on its own, so
// the owner who submits a form hears what is wrong without having to find it. Renders nothing when there
// is none. A group of controls that shares one message (a fieldset) uses this directly and points the
// group's aria-describedby at `id`.
export function FieldErrors({ id, errors, className }: { id: string; errors?: string[]; className?: string }) {
  const quiet = useContext(QuietFieldErrors);

  if (!errors || errors.length === 0) {
    return null;
  }

  return (
    <div id={id} role={quiet ? undefined : "alert"} className={cn("text-xs text-destructive", className)}>
      {errors.map((message) => (
        <p key={message}>{message}</p>
      ))}
    </div>
  );
}

// The one field primitive: label, control, hint and errors, wired together. Pass the control as a function
// to receive its ControlProps, or use the field components below, which do it for you. An error shows only
// when the caller passes one, and a caller passes one only for a field the owner has touched or submitted:
// a pristine field is empty, not wrong.
export function FieldWrapper({
  label,
  htmlFor,
  hint,
  hintClassName,
  errors,
  labelClassName,
  children,
}: {
  label: string;
  htmlFor: string;
  hint?: string;
  hintClassName?: string;
  errors?: string[];
  labelClassName?: string;
  children: ReactNode | ((control: ControlProps) => ReactNode);
}) {
  const hintId = `${htmlFor}-hint`;
  const errorId = `${htmlFor}-error`;
  const invalid = errors !== undefined && errors.length > 0;
  const describedBy = [hint ? hintId : null, invalid ? errorId : null].filter(Boolean).join(" ");
  const control: ControlProps = {
    id: htmlFor,
    ...(invalid ? { "aria-invalid": true as const } : {}),
    ...(describedBy ? { "aria-describedby": describedBy } : {}),
  };

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={htmlFor} className={labelClassName ?? "text-sm font-medium"}>
        {label}
      </label>
      {typeof children === "function" ? children(control) : children}
      {hint ? (
        <p id={hintId} className={cn("text-xs text-muted-foreground", hintClassName)}>
          {hint}
        </p>
      ) : null}
      <FieldErrors id={errorId} errors={errors} />
    </div>
  );
}

type BaseFieldProps = {
  id: string;
  label: string;
  hint?: string;
  hintClassName?: string;
  errors?: string[];
  labelClassName?: string;
};

export function TextField({
  id,
  label,
  hint,
  hintClassName,
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
    <FieldWrapper label={label} htmlFor={id} hint={hint} hintClassName={hintClassName} errors={errors} labelClassName={labelClassName}>
      {(control) => (
        <input
          value={value}
          onChange={(event) => onChange(event.target.value)}
          className={inputClasses}
          {...inputProps}
          {...control}
        />
      )}
    </FieldWrapper>
  );
}

export function TextAreaField({
  id,
  label,
  hint,
  errors,
  labelClassName,
  value,
  onChange,
  ...textareaProps
}: BaseFieldProps & {
  value: string;
  onChange: (value: string) => void;
} & Omit<ComponentProps<"textarea">, "id" | "value" | "onChange">) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      {(control) => (
        <textarea
          value={value}
          onChange={(event) => onChange(event.target.value)}
          className={inputClasses}
          {...textareaProps}
          {...control}
        />
      )}
    </FieldWrapper>
  );
}

export function NumberField({
  id,
  label,
  hint,
  hintClassName,
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
    <FieldWrapper label={label} htmlFor={id} hint={hint} hintClassName={hintClassName} errors={errors} labelClassName={labelClassName}>
      {(control) => (
        <input
          type="number"
          step={1}
          value={value}
          onChange={(event) => onChange(numberOrZero(event.target.valueAsNumber))}
          className={inputClasses}
          {...inputProps}
          {...control}
        />
      )}
    </FieldWrapper>
  );
}

// A checkbox row is the touch target, not the 16 px box: the label is tied to it, so a tap anywhere on the
// row toggles it, and the row is 44 px tall for a coarse pointer.
export function CheckboxField({
  id,
  label,
  checked,
  onChange,
  labelClassName,
  disabled,
}: {
  id: string;
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  labelClassName?: string;
  disabled?: boolean;
}) {
  return (
    <div className="flex items-center gap-2">
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
        className="size-4 rounded border bg-background disabled:opacity-50 pointer-coarse:size-6"
      />
      <label htmlFor={id} className={cn(labelClassName ?? "text-sm", "flex-1 pointer-coarse:flex pointer-coarse:min-h-11 pointer-coarse:items-center")}>
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
  placeholder,
}: BaseFieldProps & {
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
  placeholder?: string;
}) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      {(control) => (
        <select value={value} onChange={(event) => onChange(event.target.value)} className={inputClasses} {...control}>
          {placeholder ? (
            <option value="" disabled>
              {placeholder}
            </option>
          ) : null}
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      )}
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
  disabled,
}: BaseFieldProps & {
  locale: string;
  valueKop: number;
  onChange: (valueKop: number) => void;
  disabled?: boolean;
}) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      {(control) => (
        <>
          <input
            type="number"
            step={1}
            value={valueKop}
            onChange={(event) => onChange(numberOrZero(event.target.valueAsNumber))}
            disabled={disabled}
            className={inputClasses}
            {...control}
          />
          <span className="text-xs text-muted-foreground">{formatMoney(valueKop, locale)}</span>
        </>
      )}
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
  disabled,
}: BaseFieldProps & {
  locale: string;
  valueBp: number;
  onChange: (valueBp: number) => void;
  disabled?: boolean;
}) {
  return (
    <FieldWrapper label={label} htmlFor={id} hint={hint} errors={errors} labelClassName={labelClassName}>
      {(control) => (
        <>
          <input
            type="number"
            step={1}
            value={valueBp}
            onChange={(event) => onChange(numberOrZero(event.target.valueAsNumber))}
            disabled={disabled}
            className={inputClasses}
            {...control}
          />
          <span className="text-xs text-muted-foreground">{formatRate(valueBp, locale)}</span>
        </>
      )}
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
      {(control) => (
        <input
          type="text"
          value={formatMoney(valueKop, locale)}
          disabled
          className={cn(inputClasses, "text-muted-foreground")}
          {...control}
        />
      )}
    </FieldWrapper>
  );
}

export { inputClasses };
