import type { TripRequest } from '../../types/contracts';
import { today } from '../../utils/format';

export const initialDraft = (): TripRequest => ({ profileId: '', title: '悉尼，慢慢走的一天', date: today(), startTime: '09:00', endTime: '18:00', budget: 120, energy: 100, theme: 'harbour', notes: '' });
