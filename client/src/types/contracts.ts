export interface Profile {
  id: string;
  answers: number[];
  values: number[];
  character: string;
  createdAt: string;
}
export interface TripRequest {
  profileId: string;
  title: string;
  date: string;
  startTime: string;
  endTime: string;
  budget: number;
  energy: number;
  theme: string;
  notes: string;
}
export interface Stop {
  id: string;
  name: string;
  category: string;
  startTime: string;
  endTime: string;
  transferMinutes: number;
  durationMinutes: number;
  cost: number;
  walkKm: number;
  remainingHp: number;
  description: string;
}
export interface Plan {
  stops: Stop[];
  remainingHp: number;
  totalCost: number;
  walkKm: number;
  returnTime: string;
  reasons: string[];
  dataNotice: string;
}
export interface TripVersion {
  version: number;
  createdAt: string;
  easier: boolean;
  plan: Plan | null;
  conflicts: string[];
}
export interface TripSummary {
  id: string;
  title: string;
  status: 'completed' | 'infeasible' | 'failed';
  createdAt: string;
  latestVersion: number;
  request: TripRequest;
}
export interface Trip extends TripSummary { versions: TripVersion[]; }
export interface Bootstrap { profile: Profile | null; trips: TripSummary[]; demoData: boolean; }
