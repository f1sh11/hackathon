import type { ReactNode } from 'react';
import { shortDate, sydneyToday } from '../domain';
import type { Trip } from '../types';
import { Icon } from './Icon';

export type Section = 'explore' | 'saved' | 'essentials';

const navigation = [
  { id: 'explore' as const, label: '我的小冒险', icon: 'compass' },
  { id: 'saved' as const, label: '收藏的行程', icon: 'bookmark' },
  { id: 'essentials' as const, label: '出发小助手', icon: 'bag' },
];

interface Props {
  section: Section;
  onSection: (section: Section) => void;
  trips: Trip[];
  selectedId: string;
  connected: boolean;
  busy: string;
  hasProposal: boolean;
  hasTrip: boolean;
  error: string;
  notice: string;
  onNew: () => void;
  onChoose: (trip: Trip) => void;
  onReconnect: () => void;
  onDismissError: () => void;
  onDismissNotice: () => void;
  children: ReactNode;
}

/** 负责导航、页面框架与状态提示；不读取或修改服务器数据。 / Owns navigation, page framing and status messages; never accesses server data. */
export function AppLayout({
  section,
  onSection,
  trips,
  selectedId,
  connected,
  busy,
  hasProposal,
  hasTrip,
  error,
  notice,
  onNew,
  onChoose,
  onReconnect,
  onDismissError,
  onDismissNotice,
  children,
}: Props) {
  const selectionLocked = !!busy || hasProposal;
  const pageTitle =
    section === 'saved'
      ? '把想去的日子，收好。'
      : section === 'essentials'
        ? '把小事备好，把今天过好。'
        : hasTrip
          ? '今天的悉尼，按你的节奏。'
          : '小冒险，刚刚好。';
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a
          className="brand"
          href="#"
          onClick={(event) => {
            event.preventDefault();
            onSection('explore');
          }}
        >
          <span className="brand-mark">
            <Icon name="compass" size={30} />
          </span>
          <span>
            Sydney
            <span className="brand-buddy">
              Buddy<span className="brand-dot">.</span>
            </span>
          </span>
        </a>
        <p className="brand-tagline">YOUR CITY. YOUR LITTLE ADVENTURE.</p>
        <button
          type="button"
          className="button primary new-trip"
          disabled={selectionLocked}
          onClick={onNew}
        >
          <Icon name="plus" size={18} />
          开启新行程
        </button>
        <nav aria-label="主导航">
          {navigation.map((item) => (
            <button
              key={item.id}
              type="button"
              className={`nav-item ${section === item.id ? 'active' : ''}`}
              onClick={() => onSection(item.id)}
            >
              <Icon name={item.icon} />
              {item.label}
              {item.id === 'saved' && <span className="nav-count">{trips.length}</span>}
            </button>
          ))}
        </nav>
        <div className="sidebar-trips">
          <span className="eyebrow">RECENT ADVENTURES</span>
          {trips.slice(0, 4).map((trip) => (
            <button
              type="button"
              key={trip.id}
              className={trip.id === selectedId ? 'current' : ''}
              disabled={selectionLocked}
              onClick={() => onChoose(trip)}
            >
              <span className="trip-dot" />
              <span>
                <strong>{trip.title}</strong>
                <small>
                  {trip.profile.date} · {trip.days.length} 天
                </small>
              </span>
              <Icon name="arrow" size={15} />
            </button>
          ))}
          {!trips.length && <p>第一段故事，等你开始。</p>}
        </div>
        <div className="sidebar-note">
          <Icon name="leaf" size={26} />
          <p>
            不必赶完所有景点。
            <br />
            留一点时间，遇见意外。
          </p>
          <span>MADE FOR YOUR KIND OF DAY</span>
        </div>
        <div className={`connection ${connected ? 'connected' : ''}`}>
          <i />
          <span>{connected ? '旅行服务已连接' : '旅行服务未连接'}</span>
          <button
            type="button"
            className="icon-button"
            title="重新连接并刷新"
            aria-label="重新连接并刷新"
            disabled={selectionLocked}
            onClick={onReconnect}
          >
            <Icon name="refresh" size={14} />
          </button>
        </div>
      </aside>
      <main className="main-content">
        <header className="topbar">
          <span>
            SYDNEY, AUSTRALIA <span className="topbar-coordinate">33.8688° S · 151.2093° E</span>
          </span>
          <span className="topbar-date">
            <Icon name="calendar" size={15} />
            {shortDate(sydneyToday())}
          </span>
        </header>
        <div className="page-heading">
          <div>
            <span className="eyebrow">
              {section === 'explore'
                ? 'GO A LITTLE FURTHER'
                : section === 'saved'
                  ? 'PLACES TO RETURN TO'
                  : 'THE LITTLE THINGS'}
            </span>
            <h1>{pageTitle}</h1>
            <p>
              {section === 'explore'
                ? '让 Buddy 帮你串起喜欢的地方，留好散步、休息和看风景的时间。'
                : section === 'saved'
                  ? '每一次确认过的路线，都在这里等你。'
                  : '出发准备、旅行偏好，还有一路上的小小账本。'}
            </p>
          </div>
          <div className="heading-stamp">
            <Icon name="sun" size={27} />
            <span>
              TAKE IT
              <br />
              <b>AT YOUR PACE</b>
            </span>
          </div>
        </div>
        <div className="messages" aria-live="polite">
          {busy && (
            <div className="banner loading">
              <span className="spinner" />
              {busy}
            </div>
          )}
          {error && (
            <div className="banner error" role="alert">
              <span>{error}</span>
              {!connected && (
                <button
                  type="button"
                  className="text-button"
                  disabled={!!busy}
                  onClick={onReconnect}
                >
                  重新连接
                </button>
              )}
              <button
                type="button"
                className="icon-button"
                aria-label="关闭错误提示"
                onClick={onDismissError}
              >
                <Icon name="close" size={16} />
              </button>
            </div>
          )}
          {notice && (
            <div className="banner success">
              <Icon name="check" size={18} />
              <span>{notice}</span>
              <button
                type="button"
                className="icon-button"
                aria-label="关闭提示"
                onClick={onDismissNotice}
              >
                <Icon name="close" size={16} />
              </button>
            </div>
          )}
        </div>
        {children}
        <footer className="page-footer">
          <span>
            Sydney Buddy<span>·</span>慢慢走，也能走很远。
          </span>
          <span>
            Made for curious days <Icon name="leaf" size={13} />
          </span>
        </footer>
      </main>
      <nav className="mobile-nav" aria-label="手机导航">
        {navigation.map((item) => (
          <button
            type="button"
            key={item.id}
            className={section === item.id ? 'active' : ''}
            onClick={() => onSection(item.id)}
          >
            <Icon name={item.icon} size={20} />
            <span>{item.label}</span>
          </button>
        ))}
        <button type="button" disabled={selectionLocked} onClick={onNew}>
          <Icon name="plus" size={21} />
          <span>新行程</span>
        </button>
      </nav>
    </div>
  );
}
