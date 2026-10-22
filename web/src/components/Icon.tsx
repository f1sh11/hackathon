/** 统一的装饰图标，不包含业务状态。 / Shared decorative icons without business state. */
export function Icon({ name, size = 20 }: { name: string; size?: number }) {
  const paths: Record<string, string> = {
    compass: 'm16.2 7.8-2.5 5.9-5.9 2.5 2.5-5.9 5.9-2.5Z M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
    route:
      'M5 7a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z M19 21a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z M5 7v7a3 3 0 0 0 3 3h8a3 3 0 0 0 0-6h-4 M16 17h1',
    bookmark: 'M6 21V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16l-6-4-6 4Z',
    bag: 'M5 8h14l1 13H4L5 8Z M9 8V6a3 3 0 0 1 6 0v2',
    plus: 'M12 5v14 M5 12h14',
    arrow: 'M5 12h14 m-6-6 6 6-6 6',
    check: 'm5 12 4 4L19 6',
    pin: 'M20 10c0 6-8 11-8 11S4 16 4 10a8 8 0 1 1 16 0Z M15 10a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
    clock: 'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z M12 7v5l3 2',
    sun: 'M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z M12 2v2 M12 20v2 M2 12h2 M20 12h2 M5 5l1 1 M18 18l1 1 M5 19l1-1 M18 6l1-1',
    cloud: 'M7 18a5 5 0 1 1 .5-10A6 6 0 0 1 19 10a4 4 0 0 1 0 8H7Z M9 21v1 M14 21v1',
    walk: 'M14 5a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z m-4 17 2-7-3-3 2-5 3 1 3 4h3 M5 12l3-1 3-4 M12 15l4 7',
    refresh: 'M20 7v5h-5 M4 17v-5h5 M6 6a8 8 0 0 1 13 3 M18 18A8 8 0 0 1 5 15',
    trash: 'M3 6h18 M9 6V3h6v3 M5 6l1 15h12l1-15 M10 10v7 M14 10v7',
    external: 'M14 3h7v7 M21 3 10 14 M10 3H4a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1v-6',
    sparkle: 'm12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5L12 3Z',
    calendar: 'M3 5h18v16H3V5Z M7 2v6 M17 2v6 M3 10h18',
    close: 'm6 6 12 12 M18 6 6 18',
    leaf: 'M20 3C7 2 2 8 5 15s15 8 15-12Z M5 20 15 10',
  };
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.65"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={paths[name] ?? paths.compass} />
    </svg>
  );
}
