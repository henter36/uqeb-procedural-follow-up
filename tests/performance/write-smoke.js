import http from 'k6/http';
import { check, sleep } from 'k6';

const apiBase = __ENV.UQEB_API_BASE_URL || 'http://localhost:5000/api';
const username = __ENV.UQEB_TEST_USERNAME;
const password = __ENV.UQEB_TEST_PASSWORD;
const transactionId = __ENV.UQEB_TEST_TRANSACTION_ID;

export const options = {
  vus: Number(__ENV.K6_VUS || 5),
  duration: __ENV.K6_DURATION || '30s',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<2500'],
  },
};

function login() {
  const res = http.post(`${apiBase}/auth/login`, JSON.stringify({
    username,
    password,
  }), {
    headers: { 'Content-Type': 'application/json' },
  });
  check(res, { 'login ok': (r) => r.status === 200 });
  return res.json('token');
}

export default function writeSmoke() {
  if (!username || !password) {
    throw new Error('Set UQEB_TEST_USERNAME and UQEB_TEST_PASSWORD.');
  }

  const token = login();
  const headers = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  };

  if (transactionId) {
    const assignments = http.get(`${apiBase}/transactions/${transactionId}/assignments`, { headers });
    check(assignments, { 'assignments read ok': (r) => r.status === 200 });
  }

  sleep(1);
}
