import http from 'k6/http';
import { check, sleep } from 'k6';

const apiBase = __ENV.UQEB_API_BASE_URL || 'http://localhost:5000/api';
const username = __ENV.UQEB_TEST_USERNAME;
const password = __ENV.UQEB_TEST_PASSWORD;

export const options = {
  vus: Number(__ENV.K6_VUS || 10),
  duration: __ENV.K6_DURATION || '1m',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1500'],
  },
};

function requireCredentials() {
  if (!username || !password) {
    throw new Error('Set UQEB_TEST_USERNAME and UQEB_TEST_PASSWORD environment variables.');
  }
}

function login() {
  const res = http.post(`${apiBase}/auth/login`, JSON.stringify({
    username,
    password,
  }), {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'login status 200': (r) => r.status === 200,
    'token present': (r) => Boolean(r.json('token')),
  });

  return res.json('token');
}

export default function readSmoke() {
  requireCredentials();
  const token = login();
  const headers = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  };

  const dashboard = http.get(`${apiBase}/dashboard`, { headers });
  check(dashboard, { 'dashboard 200': (r) => r.status === 200 });

  const transactions = http.get(`${apiBase}/transactions?page=1&pageSize=10`, { headers });
  check(transactions, { 'transactions 200': (r) => r.status === 200 });

  const items = transactions.json('items');
  if (Array.isArray(items) && items.length > 0) {
    const id = items[0].id;
    const detail = http.get(`${apiBase}/transactions/${id}/basic`, { headers });
    check(detail, { 'transaction basic 200': (r) => r.status === 200 });
  }

  sleep(1);
}
