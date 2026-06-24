export const apiReadSmokeBaseline = {
  scenarioId: 'api-read-smoke',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1500'],
  },
  defaults: {
    vus: 10,
    duration: '1m',
  },
};
