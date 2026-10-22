import { mapLink } from '../domain';
import type { Endpoint, TripDay } from '../types';
import { Icon } from './Icon';

/** 只画地理位置示意；连线不是导航路线。 / Draws schematic positions only; lines are never navigation. */
export function RouteSketch({ day }: { day?: TripDay }) {
  const positions: Endpoint[] = day ? [day.start, ...day.stops, day.end] : [];
  const latitudes = positions.map((p) => p.lat),
    longitudes = positions.map((p) => p.lng);
  const minLat = Math.min(...latitudes, -33.88),
    maxLat = Math.max(...latitudes, -33.853);
  const minLng = Math.min(...longitudes, 151.2),
    maxLng = Math.max(...longitudes, 151.222);
  const project = (point: Endpoint) => ({
    x: 45 + ((point.lng - minLng) / (maxLng - minLng || 0.02)) * 310,
    y: 270 - ((point.lat - minLat) / (maxLat - minLat || 0.02)) * 210,
  });
  const points = positions.map(project);
  return (
    <section className="route-sketch card">
      <div className="sketch-top">
        <span className="eyebrow">A LITTLE CITY, A LOT TO FIND</span>
        <span className="mini-pill">悉尼市区</span>
      </div>
      <svg
        viewBox="0 0 400 315"
        role="img"
        aria-label={day ? '行程地点示意图，连线不是导航路线' : '悉尼港湾插画'}
      >
        <defs>
          <pattern
            id="street-grid"
            width="35"
            height="35"
            patternUnits="userSpaceOnUse"
            patternTransform="rotate(-14)"
          >
            <path d="M35 0H0V35" fill="none" stroke="#f4f1e8" strokeWidth="6" />
          </pattern>
        </defs>
        <rect width="400" height="315" fill="#e9ebdc" />
        <rect width="400" height="315" fill="url(#street-grid)" />
        <path
          d="M0 0h400v108c-36-14-45-33-65-27-22 6-16 47-47 44-24-3-14-36-48-32-25 3-43 50-72 34-24-13-21-53-45-54-44-2-54 38-123 28Z"
          fill="#bbd5cd"
        />
        <path d="M295 147c25 1 48 28 38 51-9 20-35 20-48 2-14-19-12-54 10-53Z" fill="#ccd8b9" />
        <path d="M25 249h80m-40-20v40 M200 205h65m-32-26v52" stroke="#c1cdb5" strokeWidth="2" />
        <text x="36" y="52" fill="#658e83" fontSize="12" letterSpacing="2">
          SYDNEY HARBOUR
        </text>
        <text x="256" y="178" fill="#8a9b73" fontSize="9">
          BOTANIC
        </text>
        <text x="260" y="191" fill="#8a9b73" fontSize="9">
          GARDEN
        </text>
        <text x="150" y="287" fill="#9b9d8f" fontSize="11" letterSpacing="5">
          S Y D N E Y
        </text>
        {day ? (
          <>
            <polyline
              points={points.map((p) => `${p.x},${p.y}`).join(' ')}
              fill="none"
              stroke="#205a49"
              strokeWidth="2.5"
              strokeDasharray="5 6"
              strokeLinejoin="round"
            />
            {day.stops.map((stop, i) => {
              const p = points[i + 1];
              return (
                <g key={stop.id}>
                  <circle
                    cx={p.x}
                    cy={p.y}
                    r="15"
                    fill="#173f35"
                    stroke="#fffdf8"
                    strokeWidth="3"
                  />
                  <text
                    x={p.x}
                    y={p.y + 4}
                    textAnchor="middle"
                    fill="white"
                    fontSize="11"
                    fontWeight="700"
                  >
                    {i + 1}
                  </text>
                </g>
              );
            })}
            {points[0] && (
              <g>
                <circle
                  cx={points[0].x}
                  cy={points[0].y}
                  r="5"
                  fill="#db9a5d"
                  stroke="white"
                  strokeWidth="2"
                />
                <text x={points[0].x + 10} y={points[0].y - 8} fontSize="10" fill="#8e613b">
                  出发 / 返回
                </text>
              </g>
            )}
          </>
        ) : (
          <g stroke="#315b4b" strokeWidth="2" fill="#faf7eb">
            <path d="m175 110 23-35 9 35 17-41 7 41 18-26 2 26Z" />
            <path d="M164 115h98" />
            <path d="M42 99V69m0 4c24-33 64-33 89 0m-89 0h89m0-4v30" fill="none" />
            <circle cx="190" cy="197" r="30" fill="#e9b97c" stroke="none" />
            <path d="m173 193 10-8m24 8-10-8 M179 204q11 12 23-1" fill="none" strokeWidth="3" />
            <path d="M173 177q-13-19-21-10m52 8q13-16 21-6" fill="none" />
            <circle cx="177" cy="193" r="2" fill="#315b4b" />
            <circle cx="204" cy="193" r="2" fill="#315b4b" />
          </g>
        )}
      </svg>
      <div className="sketch-bottom">
        <span>
          <Icon name="pin" size={15} />
          {day ? `${day.stops.length} 个地点 · 位置示意` : '从港湾开始，跟着好奇心走'}
        </span>
        <small>示意连线，非导航底图</small>
      </div>
      {day && (
        <a className="map-open" href={mapLink(day.start)} target="_blank" rel="noreferrer">
          在 Google Maps 打开起点
          <Icon name="external" size={14} />
        </a>
      )}
    </section>
  );
}
