import http from 'k6/http';
import { check } from 'k6';

export function requireCredentials(username, password) {
  if (!username || !password) {
    throw new Error('Set UQEB_TEST_USERNAME and UQEB_TEST_PASSWORD environment variables.');
  }
}

export function login(apiBase, username, password) {
  const response = http.post(
    `${apiBase}/auth/login`,
    JSON.stringify({
      username,
      password,
    }),
    {
      headers: {
        'Content-Type': 'application/json',
      },
      tags: {
        endpoint: 'auth-login',
      },
    },
  );

  let token = null;

  try {
    token = response.json('token');
  } catch {
    token = null;
  }

  const valid = check(response, {
    'login status 200': (res) => res.status === 200,
    'login token present': () =>
      typeof token === 'string' && token.length > 0,
  });

  if (!valid || !token) {
    throw new Error(`Login failed: status=${response.status}`);
  }

  return token;
}
