import { useState } from 'react';
import type { FormEvent } from 'react';
import { money, parseCents, splitCents } from '../domain';
import { useLocalState } from '../hooks';
import type { Expense, Memory, Trip } from '../types';
import { Icon } from './Icon';

const preparationItems = [
  {
    key: 'route',
    title: '看过今天的路线',
    detail: '确认出发地、返回地与必去景点。',
    icon: 'route',
  },
  { key: 'weather', title: '刷新出发天气', detail: '打开行程，刷新一次真实天气。', icon: 'sun' },
  {
    key: 'packing',
    title: '随身物品准备好了',
    detail: '水、舒适的鞋、充电宝，以及一点好奇心。',
    icon: 'bag',
  },
];
interface Props {
  trip?: Trip;
  memories: Memory[];
  busy: boolean;
  onRemember: () => void;
  onForget: (id: string) => void;
  onError: (message: string) => void;
  onShowTrip: () => void;
  weatherChecked: boolean;
}
/** 管理本机准备清单和整数分账本，服务器偏好由回调处理。 / Owns local checklists and integer-cent expenses; delegates server preferences. */
export function Essentials({
  trip,
  memories,
  busy,
  onRemember,
  onForget,
  onError,
  onShowTrip,
  weatherChecked,
}: Props) {
  const [checked, setChecked] = useLocalState<Record<string, string[]>>(
    'sydneybuddy.preparations',
    {},
  );
  const [ledger, setLedger] = useLocalState<Record<string, Expense[]>>('sydneybuddy.expenses', {});
  const [consent, setConsent] = useState(false);
  const [title, setTitle] = useState('');
  const [amount, setAmount] = useState('');
  const [people, setPeople] = useState(2);
  const id = trip?.id ?? '';
  const complete = checked[id] ?? [];
  const entries = ledger[id] ?? [];
  function toggle(key: string) {
    if (!id) return;
    setChecked({
      ...checked,
      [id]: complete.includes(key) ? complete.filter((k) => k !== key) : [...complete, key],
    });
  }
  function addExpense(e: FormEvent) {
    e.preventDefault();
    try {
      if (!id) throw new Error('请先确认并保存一份行程');
      const cents = parseCents(amount);
      splitCents(cents, people);
      const next = { id: crypto.randomUUID(), title: title.trim() || '旅行消费', cents, people };
      setLedger({ ...ledger, [id]: [...entries, next] });
      setTitle('');
      setAmount('');
    } catch (error) {
      onError(error instanceof Error ? error.message : '金额无效');
    }
  }
  return (
    <div className="essentials-grid">
      <section className="card preparation-card">
        <div className="section-heading">
          <div>
            <span className="eyebrow">READY WHEN YOU ARE</span>
            <h2>准备好，就出发</h2>
          </div>
          <span className="round-icon">
            <Icon name="bag" />
          </span>
        </div>
        <p className="muted">
          {trip
            ? `${trip.title} · ${trip.profile.date}`
            : '保存一份行程，准备清单就会跟着它一起保存。'}
        </p>
        <div className="preparation-list">
          {preparationItems.map((item) => (
            <div className="preparation-item" key={item.key}>
              <button
                type="button"
                className={`preparation-toggle ${complete.includes(item.key) || (item.key === 'weather' && weatherChecked) ? 'done' : ''}`}
                disabled={!id || item.key === 'weather'}
                onClick={() => toggle(item.key)}
                aria-pressed={complete.includes(item.key)}
                aria-label={item.title}
              >
                {complete.includes(item.key) || (item.key === 'weather' && weatherChecked) ? (
                  <Icon name="check" size={17} />
                ) : (
                  <Icon name={item.icon} size={17} />
                )}
              </button>
              <div>
                <strong>{item.title}</strong>
                <small>{item.detail}</small>
                {item.key === 'weather' && (
                  <button className="text-button" type="button" disabled={!id} onClick={onShowTrip}>
                    去行程刷新天气
                    <Icon name="arrow" size={13} />
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
        <p className="local-note">准备清单保存在此浏览器，按行程分别记录。</p>
      </section>
      <section className="card memories-card">
        <div className="section-heading">
          <div>
            <span className="eyebrow">GETTING TO KNOW YOU</span>
            <h2>记住你的旅行节奏</h2>
          </div>
          <Icon name="leaf" size={25} />
        </div>
        <p className="muted">
          保存兴趣和步数偏好，供下一次在线推荐参考。你可以随时撤回；历史审计记录仍会保留。
        </p>
        <label className="check-label consent-label">
          <input
            type="checkbox"
            checked={consent}
            disabled={!id || busy}
            onChange={(e) => setConsent(e.target.checked)}
          />
          我同意保存这份行程的旅行偏好
        </label>
        <button
          type="button"
          className="button secondary"
          disabled={!id || !consent || busy}
          onClick={() => {
            onRemember();
            setConsent(false);
          }}
        >
          <Icon name="bookmark" size={16} />
          保存已同意的偏好
        </button>
        <div className="memory-list">
          {memories.length ? (
            memories.map((memory) => (
              <article key={memory.id}>
                <span className="memory-leaf">
                  <Icon name="leaf" size={16} />
                </span>
                <div>
                  <p>{memory.summary}</p>
                  <small>你确认保存的偏好</small>
                </div>
                <button
                  type="button"
                  className="icon-button danger"
                  title="撤回这条偏好"
                  aria-label={`撤回偏好：${memory.summary}`}
                  disabled={busy}
                  onClick={() => onForget(memory.id)}
                >
                  <Icon name="trash" size={16} />
                </button>
              </article>
            ))
          ) : (
            <p className="empty-note">还没有保存偏好。Buddy 会先听你这次怎么说。</p>
          )}
        </div>
      </section>
      <section className="card ledger-card">
        <div className="section-heading">
          <div>
            <span className="eyebrow">SHARE THE DAY</span>
            <h2>一起出门，轻松分账</h2>
          </div>
          <div className="ledger-total">
            <small>已记总额</small>
            <strong>{money(entries.reduce((sum, entry) => sum + entry.cents, 0))}</strong>
          </div>
        </div>
        <form onSubmit={addExpense} className="expense-form">
          <fieldset disabled={!id}>
            <label>
              消费项目
              <input
                maxLength={60}
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="比如：午餐、咖啡"
              />
            </label>
            <label>
              金额 · AUD
              <input
                inputMode="decimal"
                required
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                placeholder="0.00"
              />
            </label>
            <label>
              分摊人数
              <input
                type="number"
                required
                min="1"
                max="10"
                step="1"
                value={people}
                onChange={(e) => setPeople(Number(e.target.value))}
              />
            </label>
            <button className="button primary" type="submit">
              <Icon name="plus" size={17} />
              记一笔
            </button>
          </fieldset>
        </form>
        <div className="expenses">
          {entries.map((entry) => {
            const shares = splitCents(entry.cents, entry.people);
            const unique = [...new Set(shares)];
            return (
              <article key={entry.id}>
                <span className="expense-icon">
                  <Icon name="bag" size={18} />
                </span>
                <div>
                  <strong>{entry.title}</strong>
                  <small>
                    {entry.people} 人分摊 · 每人 {unique.map(money).join(' / ')}
                    {unique.length > 1 && '（余分分配给前几位）'}
                  </small>
                </div>
                <b>{money(entry.cents)}</b>
                <button
                  type="button"
                  className="icon-button danger"
                  aria-label={`删除消费：${entry.title}`}
                  onClick={() =>
                    setLedger({ ...ledger, [id]: entries.filter((item) => item.id !== entry.id) })
                  }
                >
                  <Icon name="trash" size={15} />
                </button>
              </article>
            );
          })}
          {!entries.length && (
            <p className="empty-note">
              {id ? '还没有消费记录，把第一杯咖啡记下来吧。' : '先保存行程，就能开始记录消费。'}
            </p>
          )}
        </div>
        <p className="local-note">金额按整数分计算，余分逐一分配。账本仅保存在此浏览器。</p>
      </section>
    </div>
  );
}
