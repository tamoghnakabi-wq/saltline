import { CopyButton } from "@/components/CopyButton";
import { fileFor, isPublished, longDate, megabytes, release, type ReleaseFile } from "@/lib/release";

const platforms: { id: ReleaseFile["platform"]; title: string; needs: string }[] = [
  { id: "macos-arm64", title: "macOS", needs: "Apple silicon (M1 or later), macOS 12 or later" },
  { id: "windows-x64", title: "Windows", needs: "64-bit Windows 10 or 11, DirectX 11 or 12 graphics" },
];

function PlatformCard({ id, title, needs }: (typeof platforms)[number]) {
  const file = fileFor(id);
  return (
    <div className="card flex flex-col gap-5 p-6">
      <div className="flex items-baseline justify-between gap-4">
        <h3 className="text-2xl font-black tracking-tight">{title}</h3>
        {file && <span className="font-mono text-sm text-dim">{megabytes(file.size)}</span>}
      </div>
      <p className="text-sm text-dim">{needs}</p>
      {file ? (
        <>
          <div className="flex flex-col gap-2">
            <span className="font-mono text-xs uppercase tracking-wider text-faint">File</span>
            <span className="break-all font-mono text-sm text-fg">{file.name}</span>
          </div>
          <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between gap-3">
              <span className="font-mono text-xs uppercase tracking-wider text-faint">SHA-256</span>
              <CopyButton text={file.sha256} />
            </div>
            <code className="break-all font-mono text-[13px] leading-relaxed text-dim">{file.sha256}</code>
          </div>
          {file.url ? (
            <a className="btn-primary mt-auto" href={file.url} download>
              Download for {title}
            </a>
          ) : (
            <span className="btn mt-auto cursor-not-allowed border border-line text-faint" aria-disabled="true">
              Not published yet
            </span>
          )}
        </>
      ) : (
        <span className="btn mt-auto cursor-not-allowed border border-line text-faint" aria-disabled="true">
          No build packaged yet
        </span>
      )}
    </div>
  );
}

export function Downloads() {
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2 font-mono text-sm text-dim">
        <span>
          Version <span className="text-fg">{release.version}</span>
        </span>
        {release.released && <span>{longDate(release.released)}</span>}
        <span>Network protocol {release.protocol}</span>
        {release.releasePage && (
          <a className="text-accent underline-offset-4 hover:underline" href={release.releasePage}>
            Release page
          </a>
        )}
        {release.checksums && (
          <a className="text-accent underline-offset-4 hover:underline" href={release.checksums}>
            SHA256SUMS.txt
          </a>
        )}
      </div>
      {!isPublished && (
        <p className="rounded-md border border-accent/30 bg-accent/10 px-4 py-3 text-sm text-fg">
          {release.files.length > 0
            ? "These builds have been packaged and checked, but not published. The download buttons appear when they are."
            : "No release has been published yet."}
        </p>
      )}
      <div className="grid gap-5 md:grid-cols-2">
        {platforms.map((p) => (
          <PlatformCard key={p.id} {...p} />
        ))}
      </div>
    </div>
  );
}
