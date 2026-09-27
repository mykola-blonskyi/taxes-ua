import { Fragment, type ReactNode } from "react";
import { cn } from "@/shared/lib/utils";

export type PeriodColumn<Row> = {
  key: string;
  header: string;
  cell: (row: Row) => ReactNode;
  numeric?: boolean;
};

export function PeriodTable<Row>({
  rows,
  columns,
  rowKey,
  rowHeader,
  rowHeaderLabel,
}: {
  rows: Row[];
  columns: PeriodColumn<Row>[];
  rowKey: (row: Row) => string;
  rowHeader: (row: Row) => ReactNode;
  rowHeaderLabel: string;
}) {
  return (
    <>
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b text-left align-bottom text-xs text-muted-foreground">
              <th scope="col" className="px-1 py-2 font-medium">
                {rowHeaderLabel}
              </th>
              {columns.map((column) => (
                <th
                  key={column.key}
                  scope="col"
                  className={cn("px-1 py-2 font-medium", column.numeric ? "text-right" : undefined)}
                >
                  {column.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={rowKey(row)} className="border-b align-top">
                <th scope="row" className="px-1 py-2 text-left font-medium">
                  {rowHeader(row)}
                </th>
                {columns.map((column) => (
                  <td
                    key={column.key}
                    className={cn("px-1 py-2", column.numeric ? "whitespace-nowrap text-right tabular-nums" : undefined)}
                  >
                    {column.cell(row)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ul className="flex flex-col gap-2 md:hidden">
        {rows.map((row) => (
          <li key={rowKey(row)} className="rounded-lg border p-3">
            <div className="text-sm font-semibold">{rowHeader(row)}</div>
            <dl className="mt-2 grid grid-cols-2 gap-x-2 gap-y-1 text-sm">
              {columns.map((column) => (
                <Fragment key={column.key}>
                  <dt className="text-muted-foreground">{column.header}</dt>
                  <dd className={cn("text-right", column.numeric ? "tabular-nums" : undefined)}>
                    {column.cell(row)}
                  </dd>
                </Fragment>
              ))}
            </dl>
          </li>
        ))}
      </ul>
    </>
  );
}
