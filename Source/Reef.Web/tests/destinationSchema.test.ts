import { describe, expect, it } from 'vitest';
import { fromStored } from '../src/lib/destinationSchema';

describe('fromStored', () => {
  it('matches select values to their option regardless of case', () => {
    const config = fromStored('Email', JSON.stringify({ EmailProvider: 'SMTP', SecurityMode: 'starttls' }));

    expect(config.EmailProvider).toBe('Smtp');
    expect(config.SecurityMode).toBe('StartTls');
  });

  it('reads the legacy SmtpHost key as the smtp host', () => {
    expect(fromStored('Email', JSON.stringify({ SmtpHost: 'mail.example.com' })).SmtpServer).toBe('mail.example.com');
  });
});
