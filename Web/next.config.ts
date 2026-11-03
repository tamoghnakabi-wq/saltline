import type { NextConfig } from "next";

// A static site: every page is HTML at build time, so it can be hosted anywhere (Vercel, GitHub Pages)
// with no server code. /version.json is a plain file in public/.
const config: NextConfig = {
  output: "export",
  images: { unoptimized: true },
  poweredByHeader: false,
};

export default config;
