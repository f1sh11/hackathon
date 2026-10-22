import { useState } from 'react';
import { shortDate, stopIds, tripSteps } from '../domain';
import type { Trip } from '../types';
import { Icon } from './Icon';

interface Props {
  trips: Trip[];
  busy: boolean;
  hasProposal: boolean;
  onChoose: (trip: Trip) => void;
  onDelete: (id: string) => void;
  onNew: () => void;
}

/** 只展示行程集合并管理删除确认；真正的删除交给调用方。 / Displays the trip collection and local delete confirmation; delegates persistence. */
export function SavedTrips({ trips, busy, hasProposal, onChoose, onDelete, onNew }: Props) {
  const [pendingDeleteId, setPendingDeleteId] = useState('');
  const selectionLocked = busy || hasProposal;
  return (
    <div className="saved-grid">
      {trips.map((trip) => (
        <article className="saved-card card" key={trip.id}>
          <div className="saved-illustration">
            <Icon name="compass" size={49} />
            <span>
              {trip.days.length} DAY{trip.days.length > 1 ? 'S' : ''}
            </span>
          </div>
          <div className="saved-card-body">
            <span className="eyebrow">
              {trip.mode === 'demo' ? 'DEMO ADVENTURE' : 'YOUR ADVENTURE'}
            </span>
            <h2>{trip.title}</h2>
            <p>{shortDate(trip.profile.date)}</p>
            <div className="saved-stats">
              <span>{stopIds(trip).length} 个地点</span>
              <span>{tripSteps(trip).toLocaleString()} 步</span>
            </div>
            <div className="saved-actions">
              <button
                type="button"
                className="button secondary"
                disabled={selectionLocked}
                onClick={() => onChoose(trip)}
              >
                打开行程
                <Icon name="arrow" size={15} />
              </button>
              {pendingDeleteId === trip.id ? (
                <>
                  <button
                    type="button"
                    className="text-button danger"
                    disabled={busy}
                    onClick={() => onDelete(trip.id!)}
                  >
                    确认删除
                  </button>
                  <button
                    type="button"
                    className="icon-button"
                    aria-label="取消删除"
                    onClick={() => setPendingDeleteId('')}
                  >
                    <Icon name="close" size={15} />
                  </button>
                </>
              ) : (
                <button
                  type="button"
                  className="icon-button danger"
                  disabled={selectionLocked}
                  title="删除行程"
                  aria-label={`删除 ${trip.title} ${trip.profile.date}`}
                  onClick={() => setPendingDeleteId(trip.id!)}
                >
                  <Icon name="trash" size={17} />
                </button>
              )}
            </div>
          </div>
        </article>
      ))}
      {!trips.length && (
        <section className="card saved-empty">
          <Icon name="bookmark" size={38} />
          <h2>这里还空着，正好。</h2>
          <p>规划一份小冒险，确认后就会出现在这里。</p>
          <button
            type="button"
            className="button primary"
            disabled={selectionLocked}
            onClick={onNew}
          >
            开始第一份行程
            <Icon name="arrow" size={17} />
          </button>
        </section>
      )}
    </div>
  );
}
