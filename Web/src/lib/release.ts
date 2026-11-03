import data from "@/data/release.json";

export type ReleaseFile = {
  platform: "macos-arm64" | "windows-x64";
  label: string;
  name: string;
  size: number;
  sha256: string;
  url: string | null;
  built?: string | null;
};

export type Release = {
  game: string;
  version: string;
  tag: string;
  protocol: number;
  minimumProtocol: number;
  minimumVersion: string | null;
  released: string | null;
  commit: string | null;
  repo: string | null;
  releasePage: string | null;
  checksums: string | null;
  notes: string;
  files: ReleaseFile[];
};

// Written by Tools/package_release.py from the packaged builds. Until a release is packaged it lists no files.
export const release = data as Release;

export const isPublished = release.files.length > 0 && release.files.every((f) => !!f.url);

export function fileFor(platform: ReleaseFile["platform"]): ReleaseFile | undefined {
  return release.files.find((f) => f.platform === platform);
}

export function megabytes(bytes: number): string {
  return `${(bytes / 1024 / 1024).toFixed(0)} MB`;
}

export function longDate(iso: string | null): string {
  if (!iso) return "";
  const d = new Date(`${iso}T00:00:00Z`);
  return d.toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric", timeZone: "UTC" });
}

export function repoFile(path: string): string | null {
  return release.repo ? `https://github.com/${release.repo}/blob/${release.tag}/${path}` : null;
}
