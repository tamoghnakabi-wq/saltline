import { Downloads } from "@/components/Downloads";
import { Notes } from "@/components/Notes";
import { isPublished, release, repoFile } from "@/lib/release";

const nav = [
  ["How it plays", "#game"],
  ["Map", "#map"],
  ["Weapons", "#weapons"],
  ["Download", "#download"],
  ["Limitations", "#limitations"],
];

const rules = [
  {
    title: "Team deathmatch",
    body: "Two teams of four. The first team to 50 kills wins, or the team ahead when the 10 minutes run out. You respawn after 5 seconds, with 2 seconds of protection.",
  },
  {
    title: "Movement",
    body: "Sprint, crouch, slide out of a sprint and jump. Play in first person, or press V for an over-the-shoulder view.",
  },
  {
    title: "Damage",
    body: "Headshots hit harder and damage falls off with range, differently for each weapon. Health comes back 6 seconds after you were last hit.",
  },
  {
    title: "Online",
    body: "One player hosts from the menu. Up to seven others join with the host's code or from the list of open matches. Connections go through Unity Relay, so nobody has to open ports.",
  },
];

const lanes = [
  { name: "North: the salt pans", body: "Open brine pans with storage tanks for cover, a sunken channel you can drop into, and a raised catwalk." },
  { name: "Middle: the pump house", body: "A two-storey concrete building. The upper floor has windows over both courtyards." },
  { name: "South: the dock", body: "Sheds that bullets pass through, salt piles, a raised dock platform, a crane and a cliff road above the sea." },
];

const weapons = [
  { name: "VK-21 Halyard", kind: "Assault rifle", img: "w-halyard", damage: "26", rpm: 750, mag: 30, slot: "Primary" },
  { name: "SMR-9 Wasp", kind: "Submachine gun", img: "w-wasp", damage: "22", rpm: 900, mag: 32, slot: "Primary" },
  { name: "TS-12 Breaker", kind: "Pump shotgun", img: "w-breaker", damage: "9 × 14", rpm: 70, mag: 6, slot: "Primary" },
  { name: "LR-338 Longreach", kind: "Sniper rifle", img: "w-longreach", damage: "95", rpm: 45, mag: 5, slot: "Primary" },
  { name: "P-45 Brace", kind: "Pistol", img: "w-brace", damage: "34", rpm: 400, mag: 12, slot: "Sidearm" },
];

const gallery = [
  { img: "north-staging", alt: "Brine tanks and barrels in the north staging yard" },
  { img: "pans", alt: "Salt pans between rusted storage tanks" },
  { img: "operator", alt: "An operator of the blue team holding a VK-21 Halyard" },
  { img: "channel", alt: "A brine channel running under the pump house" },
  { img: "cliff-road", alt: "A dump truck on the cliff road above the sea" },
  { img: "red-exit", alt: "A salt truck outside the red team's spawn" },
];

const limitations = [
  "The Windows build has not been run on Windows hardware yet. It is built from the same project and version as the Mac build, but no one has played it.",
  "Online play has been tested over Unity Relay between several copies of the game on one Mac. Players on separate machines, and a Mac playing with a Windows PC, have not been tested.",
  "The host's copy of the game runs the match. The host could cheat, and the match ends for everyone if the host leaves. Play with people you trust.",
  "The Mac app is not notarized by Apple and the Windows executable is not code-signed, so both systems warn you the first time you open the game.",
  "One map and one mode. Frame rates have been measured on one machine, a MacBook Air M4.",
  "On first launch the game prepares its shaders in the main menu, which stutters for a few seconds. On Windows this step is not set up yet, so short pauses can happen the first time effects appear in a match.",
];

function Section({ id, eyebrow, title, children }: { id: string; eyebrow: string; title: string; children: React.ReactNode }) {
  return (
    <section id={id} className="border-t border-line">
      <div className="mx-auto flex max-w-6xl flex-col gap-10 px-4 py-20 sm:px-6 sm:py-24">
        <div className="flex max-w-2xl flex-col gap-3">
          <span className="eyebrow">{eyebrow}</span>
          <h2 className="h2">{title}</h2>
        </div>
        {children}
      </div>
    </section>
  );
}

export default function Home() {
  const attributions = repoFile("ATTRIBUTIONS.md");
  return (
    <>
      <header className="fixed inset-x-0 top-0 z-50 border-b border-line bg-ink/80 backdrop-blur-md">
        <div className="mx-auto flex h-14 max-w-6xl items-center justify-between gap-6 px-4 sm:px-6">
          <a href="#top" className="text-lg font-black uppercase tracking-[0.2em]">
            Saltline
          </a>
          <nav aria-label="Sections" className="hidden items-center gap-6 text-sm font-medium text-dim md:flex">
            {nav.map(([label, href]) => (
              <a key={href} href={href} className="transition-colors hover:text-fg">
                {label}
              </a>
            ))}
          </nav>
          <a href="#download" className="rounded-md bg-accent px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-ink hover:bg-accent-hover">
            Download
          </a>
        </div>
      </header>

      <main id="top">
        <section className="relative isolate flex min-h-[min(92svh,960px)] items-end overflow-hidden">
          <picture>
            <source media="(max-width: 800px)" srcSet="/media/hero-sm.webp" />
            <img
              src="/media/hero.webp"
              alt="A yellow-railed catwalk between rusted brine tanks at Kappa Saltworks"
              className="absolute inset-0 -z-20 h-full w-full object-cover"
              fetchPriority="high"
            />
          </picture>
          <div className="absolute inset-0 -z-10 bg-gradient-to-t from-ink via-ink/45 to-transparent" />
          <div className="absolute inset-0 -z-10 bg-gradient-to-r from-ink/75 via-ink/10 to-transparent" />
          <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 pb-16 pt-32 sm:px-6 sm:pb-24">
            <span className="eyebrow">4v4 team deathmatch · macOS and Windows</span>
            <h1 className="text-6xl font-black uppercase leading-none tracking-tight sm:text-8xl">Saltline</h1>
            <p className="max-w-xl text-lg leading-relaxed text-fg/85 sm:text-xl">
              Eight players, one salt works on a cliff above the sea. Fight through the brine pans, the pump house
              and the dock in ten-minute team deathmatch.
            </p>
            <div className="flex flex-wrap gap-3">
              <a href="#download" className="btn-primary">
                Download
              </a>
              <a href="#game" className="btn-ghost">
                How it plays
              </a>
            </div>
            <p className="font-mono text-xs text-dim">
              Version {release.version} · {isPublished ? "test release" : "not released yet"} · free
            </p>
          </div>
        </section>

        <Section id="game" eyebrow="How it plays" title="Short matches, one map, five guns">
          <div className="grid gap-px overflow-hidden rounded-lg border border-line bg-line sm:grid-cols-2">
            {rules.map((r) => (
              <div key={r.title} className="flex flex-col gap-2 bg-panel p-6 sm:p-8">
                <h3 className="text-lg font-bold">{r.title}</h3>
                <p className="leading-relaxed text-dim">{r.body}</p>
              </div>
            ))}
          </div>
        </Section>

        <Section id="map" eyebrow="The map" title="Kappa Saltworks">
          <div className="grid items-center gap-10 lg:grid-cols-[1fr_1fr]">
            <div className="flex flex-col gap-6">
              <p className="lead">
                A salt evaporation plant on a cliff above the sea, 116 × 72 m and mirror-symmetric, so both teams get
                the same routes. Three lanes run between the spawns.
              </p>
              <dl className="flex flex-col gap-4">
                {lanes.map((l) => (
                  <div key={l.name} className="border-l-2 border-accent/60 pl-4">
                    <dt className="font-bold">{l.name}</dt>
                    <dd className="mt-1 leading-relaxed text-dim">{l.body}</dd>
                  </div>
                ))}
              </dl>
            </div>
            <img
              src="/media/topdown.webp"
              alt="Top-down view of Kappa Saltworks: salt pans at the top, the pump house in the middle, the dock at the bottom"
              className="aspect-square w-full rounded-lg border border-line object-cover"
              loading="lazy"
            />
          </div>
          <img
            src="/media/overview.webp"
            alt="Kappa Saltworks seen from the hill to the south"
            className="aspect-video w-full rounded-lg border border-line object-cover"
            loading="lazy"
          />
        </Section>

        <Section id="weapons" eyebrow="Weapons" title="Pick a primary, keep the pistol">
          <p className="lead -mt-4 max-w-2xl">
            Choose one of four primaries; the P-45 Brace is always your sidearm. A new choice applies straight away
            while you are still protected after spawning, otherwise on your next life.
          </p>
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {weapons.map((w) => (
              <article key={w.name} className="card overflow-hidden">
                <img src={`/media/${w.img}.webp`} alt={w.name} className="aspect-[1100/430] w-full object-cover" loading="lazy" />
                <div className="flex flex-col gap-4 p-5">
                  <div className="flex items-baseline justify-between gap-3">
                    <h3 className="text-lg font-bold">{w.name}</h3>
                    <span className="font-mono text-xs uppercase tracking-wider text-faint">{w.slot}</span>
                  </div>
                  <p className="-mt-3 text-sm text-dim">{w.kind}</p>
                  <dl className="grid grid-cols-3 gap-3 border-t border-line pt-4 font-mono text-sm">
                    <div>
                      <dt className="text-[11px] uppercase tracking-wider text-faint">Damage</dt>
                      <dd className="mt-1 text-fg">{w.damage}</dd>
                    </div>
                    <div>
                      <dt className="text-[11px] uppercase tracking-wider text-faint">Rounds/min</dt>
                      <dd className="mt-1 text-fg">{w.rpm}</dd>
                    </div>
                    <div>
                      <dt className="text-[11px] uppercase tracking-wider text-faint">Magazine</dt>
                      <dd className="mt-1 text-fg">{w.mag}</dd>
                    </div>
                  </dl>
                </div>
              </article>
            ))}
          </div>
        </Section>

        <section aria-label="Screenshots" className="border-t border-line">
          <div className="mx-auto grid max-w-6xl gap-3 px-4 py-20 sm:grid-cols-2 sm:px-6 lg:grid-cols-3">
            {gallery.map((g) => (
              <figure key={g.img} className="overflow-hidden rounded-lg border border-line">
                <img src={`/media/${g.img}.webp`} alt={g.alt} className="aspect-video w-full object-cover" loading="lazy" />
              </figure>
            ))}
            <p className="font-mono text-xs text-faint sm:col-span-2 lg:col-span-3">
              Screenshots rendered by the game on a MacBook Air M4, without editing.
            </p>
          </div>
        </section>

        <Section id="download" eyebrow="Download" title="Get the game">
          <Downloads />
          <div className="grid gap-5 lg:grid-cols-2">
            <div className="card flex flex-col gap-4 p-6">
              <h3 className="text-lg font-bold">Install on a Mac</h3>
              <ol className="flex list-decimal flex-col gap-2 pl-5 leading-relaxed text-dim marker:text-faint">
                <li>Open the downloaded zip, then drag SALTLINE to your Applications folder.</li>
                <li>
                  Open SALTLINE. The first time, macOS refuses to open it because it is not notarized by Apple. Close
                  the message.
                </li>
                <li>
                  Open System Settings, then Privacy &amp; Security. Under Security, next to the message that SALTLINE
                  was blocked, choose Open Anyway, then confirm with your password.
                </li>
              </ol>
              <p className="text-sm leading-relaxed text-faint">
                You only need to do this once. If there is no Open Anyway button, you can instead clear the download
                flag for SALTLINE alone in Terminal, then open it again:
              </p>
              <code className="code block overflow-x-auto whitespace-pre">xattr -dr com.apple.quarantine /Applications/SALTLINE.app</code>
            </div>
            <div className="card flex flex-col gap-4 p-6">
              <h3 className="text-lg font-bold">Install on Windows</h3>
              <ol className="flex list-decimal flex-col gap-2 pl-5 leading-relaxed text-dim marker:text-faint">
                <li>Right-click the downloaded zip, choose Extract All, and open the extracted folder.</li>
                <li>Run SALTLINE.exe.</li>
                <li>
                  If Windows SmartScreen says it protected your PC, choose More info, then Run anyway. The executable
                  is not code-signed.
                </li>
              </ol>
              <p className="text-sm text-faint">Keep the whole folder together: the game needs the files next to the .exe.</p>
            </div>
          </div>
          <div className="card flex flex-col gap-4 p-6">
            <h3 className="text-lg font-bold">Check the download</h3>
            <p className="leading-relaxed text-dim">
              Compare the file&apos;s SHA-256 checksum with the one shown above. If they differ, the file is damaged or
              is not the one published here: delete it and download it again.
            </p>
            <div className="grid gap-4 lg:grid-cols-2">
              <div className="flex min-w-0 flex-col gap-2">
                <span className="font-mono text-xs uppercase tracking-wider text-faint">macOS Terminal</span>
                <code className="code block overflow-x-auto whitespace-pre">shasum -a 256 ~/Downloads/SALTLINE-{release.version}-macOS-arm64.zip</code>
              </div>
              <div className="flex min-w-0 flex-col gap-2">
                <span className="font-mono text-xs uppercase tracking-wider text-faint">Windows PowerShell</span>
                <code className="code block overflow-x-auto whitespace-pre">
                  Get-FileHash $HOME\Downloads\SALTLINE-{release.version}-Windows-x64.zip
                </code>
              </div>
            </div>
          </div>
          <div className="card flex flex-col gap-4 p-6">
            <h3 className="text-lg font-bold">What it runs on</h3>
            <p className="leading-relaxed text-dim">
              Measured with the game&apos;s built-in benchmark on a MacBook Air M4 (10-core GPU, 16 GB), plugged in, full
              screen at 2940 × 1912: about 135 fps on the Laptop preset (the default on Mac laptops) and 79 fps on
              High. The game has not yet been measured on any other machine.
            </p>
            <p className="leading-relaxed text-dim">
              Online play needs an internet connection. There is no account to create: the game signs in to Unity&apos;s
              services anonymously.
            </p>
          </div>
        </Section>

        {release.notes && (
          <Section id="notes" eyebrow="Release notes" title={`What's in ${release.version}`}>
            <Notes markdown={release.notes} />
          </Section>
        )}

        <Section id="limitations" eyebrow="Before you play" title="Known limitations">
          <ul className="grid gap-4 md:grid-cols-2">
            {limitations.map((l) => (
              <li key={l} className="card p-5 leading-relaxed text-dim">
                {l}
              </li>
            ))}
          </ul>
        </Section>

        <Section id="credits" eyebrow="Credits" title="Made with free tools and assets">
          <div className="grid gap-6 text-dim md:grid-cols-2">
            <p className="leading-relaxed">
              Built in Unity 6 with the Universal Render Pipeline, Netcode for GameObjects and Unity Relay. The map,
              weapons and props were modelled in Blender for this game.
            </p>
            <p className="leading-relaxed">
              Textures: generated for the game, and CC0 from ambientCG and Poly Haven. Running, crouching, jumping and death animations: Adobe Mixamo. Fonts: Roboto and Roboto
              Mono by Google (Apache 2.0). Sound effects were synthesised for the game.
              {attributions && (
                <>
                  {" "}
                  <a className="text-accent underline-offset-4 hover:underline" href={attributions}>
                    Full attributions
                  </a>
                  .
                </>
              )}
            </p>
          </div>
        </Section>
      </main>

      <footer className="border-t border-line">
        <div className="mx-auto flex max-w-6xl flex-col gap-3 px-4 py-10 text-sm text-faint sm:flex-row sm:items-center sm:justify-between sm:px-6">
          <span>
            SALTLINE {release.version} · protocol {release.protocol}
          </span>
          <span className="flex gap-5">
            <a href="/version.json" className="hover:text-dim">
              version.json
            </a>
            {release.repo && (
              <a href={`https://github.com/${release.repo}`} className="hover:text-dim">
                Source
              </a>
            )}
          </span>
        </div>
      </footer>
    </>
  );
}
