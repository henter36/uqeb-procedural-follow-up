import http from 'k6/http';
import { check, sleep } from 'k6';
import { login, requireCredentials } from './helpers/auth.js';

const apiBase = __ENV.UQEB_API_BASE_URL || 'http://localhost:5000/api';
const username = __ENV.UQEB_TEST_USERNAME;
const password = __ENV.UQEB_TEST_PASSWORD;

// Keep duration short; setup() authenticates once and the JWT must remain valid.
export const options = {
  vus: Number(__ENV.K6_VUS || 10),
  duration: __ENV.K6_DURATION || '1m',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1500'],
  },
};

export function setup() {
  requireCredentials(username, password);
  return {
    token: login(apiBase, username, password),
  };
}

export default function readSmoke(data) {
  const headers = {
    Authorization: `Bearer ${data.token}`,
    'Content-Type': 'application/json',
  };

  const dashboard = http.get(`${apiBase}/dashboard`, {
    headers,
    tags: { endpoint: 'dashboard' },
  });
  check(dashboard, { 'dashboard 200': (r) => r.status === 200 });

  const transactions = http.get(`${apiBase}/transactions?page=1&pageSize=10`, {
    headers,
    tags: { endpoint: 'transactions-list' },
  });
  check(transactions, { 'transactions 200': (r) => r.status === 200 });

  const items = transactions.json('items');
  if (Array.isArray(items) && items.length > 0) {
    const id = items[0].id;
    const detail = http.get(`${apiBase}/transactions/${id}/basic`, {
      headers,
      tags: { endpoint: 'transaction-basic' },
    });
    check(detail, { 'transaction basic 200': (r) => r.status === 200 });
  }

  sleep(1);
}
