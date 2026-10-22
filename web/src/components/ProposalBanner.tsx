import { stopIds, tripSteps } from '../domain';
import type { Proposal, Trip } from '../types';
import { Icon } from './Icon';

interface Props {
  proposal: Proposal;
  originalTrip?: Trip;
  busy: boolean;
  onCancel: () => void;
  onAccept: () => void;
}

/** 负责草案差异与确认入口，不发请求、不改变版本。 / Displays draft differences and confirmation controls; never sends requests or changes revisions. */
export function ProposalBanner({ proposal, originalTrip, busy, onCancel, onAccept }: Props) {
  const oldIds = originalTrip ? stopIds(originalTrip) : [];
  const newIds = stopIds(proposal.trip);
  const delta = originalTrip ? tripSteps(proposal.trip) - tripSteps(originalTrip) : 0;
  const comparison = [
    `保留 ${newIds.filter((id) => oldIds.includes(id)).length} 站`,
    `新增 ${newIds.filter((id) => !oldIds.includes(id)).length} 站`,
    `移除 ${oldIds.filter((id) => !newIds.includes(id)).length} 站`,
    `总步数${delta >= 0 ? '增加' : '减少'} ${Math.abs(delta).toLocaleString()} 步`,
  ].join(' · ');
  return (
    <section className="proposal-banner">
      <div>
        <span className="eyebrow">A LITTLE PREVIEW</span>
        <h2>{proposal.targetId ? '新的安排，先看看喜不喜欢。' : '你的行程草案准备好了。'}</h2>
        <p>
          {proposal.targetId
            ? comparison
            : `${proposal.trip.days.length} 天 · ${newIds.length} 个地点 · 还没有保存`}
        </p>
      </div>
      <div className="proposal-actions">
        <button type="button" className="button ghost" disabled={busy} onClick={onCancel}>
          取消草案
        </button>
        <button type="button" className="button primary" disabled={busy} onClick={onAccept}>
          <Icon name="check" size={18} />
          确认并保存
        </button>
      </div>
    </section>
  );
}
