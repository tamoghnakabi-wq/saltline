import type { Metadata, Viewport } from "next";
import { Roboto, Roboto_Mono } from "next/font/google";
import "./globals.css";
import { release } from "@/lib/release";

const roboto = Roboto({ subsets: ["latin"], weight: ["400", "500", "700", "900"], variable: "--font-roboto", display: "swap" });
const robotoMono = Roboto_Mono({ subsets: ["latin"], weight: ["400", "700"], variable: "--font-roboto-mono", display: "swap" });

const description =
  "SALTLINE is a 4v4 team deathmatch shooter for macOS and Windows, set in a salt works on a cliff above the sea.";

// Absolute base for the social preview image: SITE_URL if set, else Vercel's production domain at build time.
const site = process.env.SITE_URL ?? (process.env.VERCEL_PROJECT_PRODUCTION_URL ? `https://${process.env.VERCEL_PROJECT_PRODUCTION_URL}` : undefined);

export const metadata: Metadata = {
  metadataBase: site ? new URL(site) : undefined,
  title: `SALTLINE ${release.version}: 4v4 team deathmatch`,
  description,
  openGraph: { title: "SALTLINE", description, images: ["/media/hero-sm.webp"], type: "website" },
  icons: { icon: "/icon.svg" },
};

export const viewport: Viewport = { themeColor: "#090b0f", width: "device-width", initialScale: 1 };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${roboto.variable} ${robotoMono.variable}`}>
      <body className="min-h-screen font-sans">{children}</body>
    </html>
  );
}
