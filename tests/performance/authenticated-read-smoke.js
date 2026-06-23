import http from 'k6/http';
import { check, sleep } from 'k6';
import { login, requireCredentials } from './helpers/auth.js';

const apiBase = __ENV.UQEB_API_BASE_URL || 'http://localhost:5000/api';
const username = __ENV.UQEB_TEST_USERNAME;
const password = __ENV.UQEB_TEST_PASSWORD;
const transactionId = __ENV.UQEB_TEST_TRANSACTION_ID;

// Authenticated read probe only; no mutation is performed.
export const options = {
  vus: Number(__ENV.K6_VUS || 5),
  duration: __ENV.K6_DURATION || '30s',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<2500'],
  },
};

export function setup() {
  requireCredentials(username, password);
  return {
    token: login(apiBase, username, password),
  };
}

export default function authenticatedReadSmoke(data) {
  const headers = {
    Authorization: `Bearer ${data.token}`,
    'Content-Type': 'application/json',
  };

  if (transactionId) {
    const assignments = http.get(`${apiBase}/transactions/${transactionId}/assignments`, {
      headers,
      tags: { endpoint: 'transaction-assignments' },
    });
    check(assignments, { 'assignments read ok': (r) => r.status === 200 });
  }

  sleep(1);
}
