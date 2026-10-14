import { RequestCapabilityStore } from './request-capability-store';

const firstRequestId = 'A'.repeat(43);
const secondRequestId = 'B'.repeat(43);
const REQUEST_IDS_KEY = 'taskpilot.audio-extraction.request-ids';
const READ_IDS_KEY = 'taskpilot.audio-extraction.read-ids';

describe('RequestCapabilityStore', () => {
  let store: RequestCapabilityStore;

  beforeEach(() => {
    localStorage.clear();
    store = new RequestCapabilityStore();
  });

  it('restores multiple request capabilities while storing no outcome or media data', () => {
    store.addRequestId(firstRequestId);
    store.addRequestId(secondRequestId);

    expect(store.getRequestIds()).toEqual([firstRequestId, secondRequestId]);
    expect(localStorage.getItem(REQUEST_IDS_KEY)).toBe(
      JSON.stringify([firstRequestId, secondRequestId]),
    );
    expect(localStorage.length).toBe(1);
    expect(localStorage.getItem(REQUEST_IDS_KEY)).not.toContain('audioPath');
    expect(localStorage.getItem(REQUEST_IDS_KEY)).not.toContain('signature');
  });

  it('persists read state locally and removes expired request capabilities', () => {
    store.addRequestId(firstRequestId);
    store.addRequestId(secondRequestId);
    store.markRead(firstRequestId);

    store.removeRequestId(firstRequestId);

    expect(store.getRequestIds()).toEqual([secondRequestId]);
    expect(store.getReadIds()).toEqual([]);
    expect(JSON.parse(localStorage.getItem(READ_IDS_KEY) ?? '[]')).toEqual([]);
  });

  it('rejects invalid IDs when restoring browser state', () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([firstRequestId, 'invalid']));

    expect(store.getRequestIds()).toEqual([firstRequestId]);
    expect(localStorage.getItem(REQUEST_IDS_KEY)).toBe(JSON.stringify([firstRequestId]));
  });
});
