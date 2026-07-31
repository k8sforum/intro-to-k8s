import type { PointOfInterest, SaveEntityResponse } from './types';

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5101';

async function throwForStatus(response: Response): Promise<void> {
  if (response.ok) return;
  const body = await response.text().catch(() => '');
  const suffix = body ? `: ${body}` : '';
  throw new Error(`${response.status} ${response.statusText}${suffix}`);
}

async function unwrap<T>(response: Response): Promise<T> {
  await throwForStatus(response);
  return response.json() as Promise<T>;
}

export function getPointsOfInterest(signal?: AbortSignal): Promise<PointOfInterest[]> {
  return fetch(`${BASE_URL}/api/PointOfInterest`, { signal }).then((r) =>
    unwrap<PointOfInterest[]>(r),
  );
}

export async function getPointOfInterestImage(
  id: number,
  resizedImage = true,
  signal?: AbortSignal,
): Promise<string> {
  const response = await fetch(
    `${BASE_URL}/api/PointOfInterest/${id}?resizedImage=${resizedImage}`,
    { signal },
  );
  await throwForStatus(response);
  // The API returns the base64 payload as text/plain, not JSON.
  return response.text();
}

export function uploadPointOfInterestImage(file: File): Promise<SaveEntityResponse> {
  const formData = new FormData();
  formData.append('image', file);
  return fetch(`${BASE_URL}/api/PointOfInterest/image`, {
    method: 'POST',
    body: formData,
  }).then((r) => unwrap<SaveEntityResponse>(r));
}
