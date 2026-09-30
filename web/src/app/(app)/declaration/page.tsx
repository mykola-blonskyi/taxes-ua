import { DeclarationScreen } from "@/features/declaration";

function single(value: string | string[] | undefined): string | undefined {
  return typeof value === "string" ? value : undefined;
}

export default async function DeclarationPage({
  searchParams,
}: {
  searchParams: Promise<{ year?: string | string[]; quarter?: string | string[] }>;
}) {
  const params = await searchParams;

  return <DeclarationScreen year={single(params.year)} quarter={single(params.quarter)} />;
}
