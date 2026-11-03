"use client";

import { useState } from "react";

export function CopyButton({ text, label = "Copy" }: { text: string; label?: string }) {
  const [state, setState] = useState<"idle" | "done" | "failed">("idle");
  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setState("done");
    } catch {
      setState("failed");
    }
    setTimeout(() => setState("idle"), 1600);
  }
  return (
    <button
      type="button"
      onClick={copy}
      className="shrink-0 rounded border border-line-strong px-2 py-1 font-mono text-[11px] font-bold uppercase tracking-wider text-dim transition-colors hover:text-fg focus-visible:outline-2 focus-visible:outline-accent"
      aria-live="polite"
    >
      {state === "done" ? "Copied" : state === "failed" ? "Select it" : label}
    </button>
  );
}
