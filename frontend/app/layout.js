import "./globals.css";

export const metadata = {
  title: "EREL Disambiguation Studio - Nền tảng Cào & Phân định Thực thể",
  description: "Hệ thống cào dữ liệu web tự động vượt Cloudflare và studio phân định thực thể EREL chuyên nghiệp.",
};

export default function RootLayout({ children }) {
  return (
    <html lang="vi">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="anonymous" />
      </head>
      <body>{children}</body>
    </html>
  );
}
