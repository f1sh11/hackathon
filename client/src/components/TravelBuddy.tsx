export function TravelBuddy({ size = 124, excited = false }: { size?: number; excited?: boolean }) {
  return <svg className="travel-buddy" width={size} height={size} viewBox="0 0 160 160" role="img" aria-label="漫漫，橙色背包旅行伙伴">
    <ellipse cx="81" cy="146" rx="49" ry="9" fill="#e8edf1" />
    <path d="M61 26v-8c0-12 38-12 38 0v8" fill="none" stroke="#c96d16" strokeWidth="10" strokeLinecap="round" />
    <path d="m34 91-15 15c-8 8-17-2-10-10l16-21M128 84l15-18c7-8 16 1 11 8l-16 25" fill="#ffa32a" stroke="#ed8b18" strokeWidth="4" strokeLinecap="round" />
    <path d="m52 129-7 12c-4 8 9 12 19 9l7-9m24-13 6 15c3 8 19 8 22 2 2-4-6-13-10-18" fill="#1cb0f6" stroke="#1596d3" strokeWidth="4" />
    <rect x="29" y="23" width="102" height="114" rx="31" fill="#ffac32" stroke="#ed8b18" strokeWidth="4" />
    <path d="M36 48c9-23 29-24 44-24h20c13 0 25 10 27 25" fill="#ffc75a" />
    <path d="M45 32v85m70-85v85" stroke="#f38c20" strokeWidth="7" strokeLinecap="round" />
    <rect x="40" y="88" width="80" height="42" rx="17" fill="#ffd657" stroke="#efaa26" strokeWidth="3" />
    <path d="M51 101h58" stroke="#f3b42d" strokeWidth="4" strokeLinecap="round" />
    <circle cx="110" cy="102" r="3" fill="#c97b18" />
    <ellipse cx="61" cy="64" rx="15" ry="19" fill="white" /><ellipse cx="99" cy="64" rx="15" ry="19" fill="white" />
    {excited ? <><path d="m61 50 3 9 8 5-8 4-3 9-4-9-8-4 8-5Z" fill="#3c3c3c" /><path d="m99 50 3 9 8 5-8 4-3 9-4-9-8-4 8-5Z" fill="#3c3c3c" /></> : <><ellipse cx="64" cy="66" rx="7" ry="10" fill="#3c3c3c" /><ellipse cx="96" cy="66" rx="7" ry="10" fill="#3c3c3c" /><circle cx="67" cy="61" r="2.5" fill="white" /><circle cx="99" cy="61" r="2.5" fill="white" /></>}
    <path d="M71 82q9 11 18 0" fill="#b85d16" stroke="#b85d16" strokeWidth="3" strokeLinecap="round" />
    <ellipse cx="45" cy="81" rx="8" ry="4" fill="#ff8354" opacity=".65" /><ellipse cx="115" cy="81" rx="8" ry="4" fill="#ff8354" opacity=".65" />
    <circle cx="80" cy="117" r="11" fill="#fff8db" stroke="#d59723" strokeWidth="2" /><path d="m80 108 4 9-4 9-4-9Z" fill="#1cb0f6" /><path d="m80 108 4 9h-8Z" fill="#ff7162" />
    {excited && <><path d="m141 21 3 7 7 3-7 3-3 7-3-7-7-3 7-3Z" fill="#ffc800" /><circle cx="20" cy="43" r="5" fill="#ce82ff" /></>}
  </svg>;
}
