import { useEffect, useRef, useState } from 'react';
import type { Profile } from '../../types/contracts';
import { axes } from './questions';

export function Radar({ profile }: { profile: Profile }) {
  const ref = useRef<SVGSVGElement>(null);
  const [width, setWidth] = useState(320);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setWidth(el.clientWidth || 320));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const radius = Math.max(35, Math.min((width - 150) / 2, 87));
  const point = (i: number, fraction: number) => [width / 2 + Math.sin(i * Math.PI / 3) * radius * fraction, 133 - Math.cos(i * Math.PI / 3) * radius * fraction];
  return <svg ref={ref} className="radar" viewBox={`0 0 ${width} 270`} role="img" aria-label="根据回答生成的只读六维旅行画像"><title>旅行角色画像，分数由问答自动生成，不能手动调整</title><desc>{axes.map((a, i) => `${a} ${profile.values[i]} 分`).join('，')}</desc>
    {[.25, .5, .75, 1].map(f => <polygon key={f} className="gridline" points={axes.map((_, i) => point(i, f).join(',')).join(' ')} />)}
    {axes.map((a, i) => { const p = point(i, 1), l = point(i, 1.28); return <g key={a}><line className="gridline" x1={width / 2} y1="133" x2={p[0]} y2={p[1]} /><text x={l[0]} y={l[1]} textAnchor={i === 0 || i === 3 ? 'middle' : i < 3 ? 'start' : 'end'} dominantBaseline="middle">{a}</text></g>; })}
    <polygon className="radar-shape" points={profile.values.map((v, i) => point(i, v / 100).join(',')).join(' ')} />
    {profile.values.map((v, i) => { const p = point(i, v / 100); return <circle key={i} className="radar-dot" cx={p[0]} cy={p[1]} r="3.5" />; })}
  </svg>;
}
