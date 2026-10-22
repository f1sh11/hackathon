import type { Bootstrap, Profile, Trip, TripRequest } from '../types/contracts';
import { api } from './http';

export function fetchBootstrap(): Promise<Bootstrap> {
  return api<Bootstrap>('/api/bootstrap');
}

export function createProfile(answers: number[]): Promise<Profile> {
  return api<Profile>('/api/profiles', { answers });
}

export function createTrip(request: TripRequest & { idempotencyKey: string }): Promise<Trip> {
  return api<Trip>('/api/trips', request);
}

export function getTrip(id: string): Promise<Trip> {
  return api<Trip>(`/api/trips/${encodeURIComponent(id)}`);
}

export function replanTrip(id: string, easier: boolean, idempotencyKey: string): Promise<Trip> {
  return api<Trip>(`/api/trips/${encodeURIComponent(id)}/replan`, { easier, idempotencyKey });
}
