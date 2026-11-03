// Renders the small subset of Markdown the release notes use: "## " headings, "- " bullets and paragraphs.
export function Notes({ markdown }: { markdown: string }) {
  const blocks: React.ReactNode[] = [];
  let items: string[] = [];
  const flush = () => {
    if (items.length) {
      blocks.push(
        <ul key={blocks.length} className="flex list-disc flex-col gap-2 pl-5 leading-relaxed text-dim marker:text-accent">
          {items.map((i) => (
            <li key={i}>{i}</li>
          ))}
        </ul>,
      );
      items = [];
    }
  };
  for (const raw of markdown.split("\n")) {
    const line = raw.trim();
    if (line.startsWith("- ")) {
      items.push(line.slice(2));
      continue;
    }
    flush();
    if (line.startsWith("## ")) blocks.push(<h3 key={blocks.length} className="mt-4 text-lg font-bold first:mt-0">{line.slice(3)}</h3>);
    else if (line) blocks.push(<p key={blocks.length} className="leading-relaxed text-dim">{line}</p>);
  }
  flush();
  return <div className="flex max-w-3xl flex-col gap-3">{blocks}</div>;
}
