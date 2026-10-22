import { useEffect, useRef, useState } from 'react';
import { api } from './api';
import { defaultProfile, savePayload, validateProfile } from './domain';
import { useLocalState } from './hooks';
import type { Health, Memory, Place, Profile, Proposal, Trip, Weather } from './types';
import { Icon } from './components/Icon';
import { PlannerForm } from './components/PlannerForm';
import { RouteSketch } from './components/RouteSketch';
import { TripView } from './components/TripView';
import { Essentials } from './components/Essentials';
import { AppLayout } from './components/AppLayout';
import type { Section } from './components/AppLayout';
import { SavedTrips } from './components/SavedTrips';
import { ProposalBanner } from './components/ProposalBanner';

/** 负责跨组件数据流和 API 操作，草案与已保存行程始终分开。 / Owns API orchestration and keeps proposals separate from saved trips. */
export default function App() {
  const [section, setSection] = useState<Section>('explore');
  const [health, setHealth] = useState<Health | null>(null);
  const [trips, setTrips] = useState<Trip[]>([]);
  const [memories, setMemories] = useState<Memory[]>([]);
  const [catalog, setCatalog] = useState<Place[]>([]);
  const [profile, setProfile] = useLocalState<Profile>('sydneybuddy.profile', defaultProfile());
  const [selectedId, setSelectedId] = useLocalState<string>('sydneybuddy.selectedTrip', '');
  const [online, setOnline] = useState(false);
  const [proposal, setProposal] = useState<Proposal | null>(null);
  const [dayIndex, setDayIndex] = useState(0);
  const [freshWeather, setFreshWeather] = useState<Weather | null>(null);
  const [weatherChecks, setWeatherChecks] = useLocalState<Record<string, boolean>>(
    'sydneybuddy.weatherChecks',
    {},
  );
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const working = useRef(false);
  const initialized = useRef(false);
  const selectedTrip = trips.find((trip) => trip.id === selectedId);
  const displayedTrip = proposal?.trip ?? selectedTrip;

  async function operation(label: string, action: () => Promise<void>) {
    if (working.current) return;
    working.current = true;
    setBusy(label);
    setError('');
    setNotice('');
    try {
      await action();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : '操作没有完成，请重试');
    } finally {
      working.current = false;
      setBusy('');
    }
  }
  async function reload(preferredId?: string, fallback = false) {
    const [status, saved, recalled, places] = await Promise.all([
      api.health(),
      api.trips(),
      api.memories(),
      api.catalog(),
    ]);
    const ordered = saved.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    setHealth(status);
    setTrips(ordered);
    setMemories(recalled);
    setCatalog(places);
    const selected =
      ordered.find((trip) => trip.id === (preferredId ?? selectedId)) ??
      (fallback ? ordered[0] : undefined);
    if (selected && (preferredId !== undefined || fallback)) {
      setSelectedId(selected.id!);
      setProfile(structuredClone(selected.profile));
    }
    if (!selected && (preferredId !== undefined || fallback)) setSelectedId('');
  }
  useEffect(() => {
    if (initialized.current) return;
    initialized.current = true;
    void operation('正在连接旅行服务…', () => reload(undefined, true));
    // Initial load only; later refreshes are explicit user actions. / 仅初始化，后续刷新由用户触发。
  }, []);

  function chooseTrip(trip: Trip) {
    if (busy || proposal) return;
    setSelectedId(trip.id!);
    setProfile(structuredClone(trip.profile));
    setFreshWeather(null);
    setDayIndex(0);
    setSection('explore');
    setError('');
  }
  function newTrip() {
    if (busy || proposal) return;
    setSelectedId('');
    setProfile(defaultProfile());
    setFreshWeather(null);
    setDayIndex(0);
    setSection('explore');
    setError('');
  }
  function generate(rain = false) {
    const source = structuredClone(profile);
    delete source.weatherOverride;
    if (rain) source.weatherOverride = 'rain';
    void operation('Buddy 正在检查地点、时间和路线…', async () => {
      validateProfile(source);
      const result = await api.plan(
        source,
        !online ? 'demo' : health?.googleConfigured ? 'live' : 'assisted',
      );
      // Snapshot the target revision now; confirmation never silently overwrites newer data. / 锁定目标版本，确认时不会悄悄覆盖新版本。
      setProposal({
        trip: result,
        targetId: selectedTrip?.id,
        expectedRevision: selectedTrip?.revision ?? 0,
      });
      setDayIndex(0);
      setFreshWeather(null);
      setSection('explore');
    });
  }
  function acceptProposal() {
    if (!proposal) return;
    const pending = proposal;
    void operation('正在保存你确认的行程…', async () => {
      const saved = await api.save(savePayload(pending));
      // 保存响应已确认成功，即使后续刷新失败也不重复提交。 / A confirmed save is final even if a later refresh fails.
      setTrips((previous) => [saved, ...previous.filter((trip) => trip.id !== saved.id)]);
      setSelectedId(saved.id!);
      setProfile(structuredClone(saved.profile));
      setProposal(null);
      setNotice('小冒险已保存，随时可以回来继续。');
    });
  }
  function cancelProposal() {
    if (busy) return;
    setProposal(null);
    setDayIndex(0);
    setFreshWeather(null);
    if (selectedTrip) setProfile(structuredClone(selectedTrip.profile));
  }
  function refreshWeather() {
    if (!displayedTrip) return;
    const source = structuredClone(displayedTrip.profile);
    delete source.weatherOverride;
    void operation('正在刷新真实天气…', async () => {
      const result = await api.weather(source);
      setFreshWeather(result);
      if (result.status === 'ok' && selectedTrip?.id && !proposal)
        setWeatherChecks((previous) => ({
          ...previous,
          [`${selectedTrip.id}:${selectedTrip.revision ?? 0}`]: true,
        }));
      setNotice(
        result.status === 'ok'
          ? '天气已刷新；已保存的路线保持原样。'
          : '天气信息暂不完整，出发前请再刷新。',
      );
    });
  }
  function remember() {
    if (!selectedTrip) return;
    void operation('正在保存已同意的偏好…', async () => {
      const p = selectedTrip.profile;
      await api.remember(
        p,
        `喜欢${p.interests.join('、') || '自由探索'}，每天步数偏好约 ${p.maxSteps} 步`,
      );
      setMemories(await api.memories());
      setNotice('偏好已保存，可随时撤回。');
    });
  }
  function forget(id: string) {
    void operation('正在撤回偏好…', async () => {
      await api.deleteMemory(id);
      setMemories(await api.memories());
      setNotice('偏好已从可检索记忆中撤回。');
    });
  }
  function removeTrip(id: string) {
    void operation('正在删除行程…', async () => {
      await api.deleteTrip(id);
      if (selectedId === id) {
        setSelectedId('');
        setProfile(defaultProfile());
      }
      await reload(selectedId === id ? '' : selectedId);
      setNotice('这份行程已删除。');
    });
  }
  return (
    <AppLayout
      section={section}
      onSection={setSection}
      trips={trips}
      selectedId={selectedId}
      connected={!!health}
      busy={busy}
      hasProposal={!!proposal}
      hasTrip={!!displayedTrip}
      error={error}
      notice={notice}
      onNew={newTrip}
      onChoose={chooseTrip}
      onReconnect={() => {
        void operation('正在重新连接…', () => reload(selectedId));
      }}
      onDismissError={() => setError('')}
      onDismissNotice={() => setNotice('')}
    >
      {section === 'explore' && (
        <>
          {proposal && (
            <ProposalBanner
              proposal={proposal}
              originalTrip={selectedTrip}
              busy={!!busy}
              onCancel={cancelProposal}
              onAccept={acceptProposal}
            />
          )}
          {!displayedTrip ? (
            <div className="onboarding-grid">
              <PlannerForm
                profile={profile}
                onChange={setProfile}
                online={online}
                onOnlineChange={setOnline}
                health={health}
                catalog={catalog}
                busy={!!busy}
                editing={false}
                onGenerate={() => generate()}
              />
              <aside className="welcome-aside">
                <RouteSketch />
                <section className="buddy-note">
                  <span className="buddy-face" aria-hidden="true">
                    ◡
                  </span>
                  <div>
                    <h3>你好，我是 Buddy。</h3>
                    <p>你说想怎么过这一天，剩下的路线我来帮你理一理。</p>
                  </div>
                </section>
                <div className="welcome-facts">
                  <span>
                    <b>01 — 03</b>天的市区漫游
                  </span>
                  <span>
                    <b>YOUR PACE</b>每一步，都留有余地
                  </span>
                </div>
              </aside>
            </div>
          ) : (
            <>
              <TripView
                trip={displayedTrip}
                dayIndex={dayIndex}
                onDayChange={setDayIndex}
                weather={freshWeather}
                busy={!!busy || !!proposal}
                onWeather={refreshWeather}
                onRain={() => generate(true)}
              />
              {!proposal && (
                <details className="adjust-panel">
                  <summary>
                    <span>
                      <Icon name="sparkle" />
                      想换个节奏？调整这份行程
                    </span>
                    <span>＋</span>
                  </summary>
                  <PlannerForm
                    profile={profile}
                    onChange={setProfile}
                    online={online}
                    onOnlineChange={setOnline}
                    health={health}
                    catalog={catalog}
                    busy={!!busy}
                    editing
                    onGenerate={() => generate()}
                  />
                </details>
              )}
            </>
          )}
        </>
      )}
      {section === 'saved' && (
        <SavedTrips
          trips={trips}
          busy={!!busy}
          hasProposal={!!proposal}
          onChoose={chooseTrip}
          onDelete={removeTrip}
          onNew={newTrip}
        />
      )}
      {section === 'essentials' && (
        <Essentials
          trip={selectedTrip}
          memories={memories}
          busy={!!busy}
          onRemember={remember}
          onForget={forget}
          onError={setError}
          onShowTrip={() => setSection('explore')}
          weatherChecked={!!weatherChecks[`${selectedId}:${selectedTrip?.revision ?? 0}`]}
        />
      )}
    </AppLayout>
  );
}
