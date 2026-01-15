import { BaseTemplateDto } from '@hsm-lib/definitions/dtos';

export function BaseTemplate(data: BaseTemplateDto): React.ReactNode {
  const BRAND_BLUE = '#06478d';

  const page: React.CSSProperties = {
    margin: 0,
    padding: 0,
    backgroundColor: '#f3f5f7',
    fontFamily:
      '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Arial, "Helvetica Neue", sans-serif',
    color: '#111827',
  };

  const outerTable: React.CSSProperties = {
    width: '100%',
    borderCollapse: 'collapse',
    backgroundColor: '#f3f5f7',
  };

  const containerCell: React.CSSProperties = {
    padding: '28px 12px',
  };

  const card: React.CSSProperties = {
    width: '100%',
    maxWidth: '720px',
    borderCollapse: 'separate',
    borderSpacing: 0,
    backgroundColor: '#ffffff',
    border: '1px solid #e5e7eb',
    borderRadius: '16px',
    overflow: 'hidden',
    boxShadow: '0 10px 30px rgba(17, 24, 39, 0.08)',
  };

  const header: React.CSSProperties = {
    backgroundColor: BRAND_BLUE,
    padding: '20px 24px',
  };

  const title: React.CSSProperties = {
    margin: 0,
    color: '#ffffff',
    fontSize: '26px',
    lineHeight: '32px',
    fontWeight: 800,
    letterSpacing: '-0.2px',
    textAlign: 'center',
  };

  const main: React.CSSProperties = {
    padding: '22px 24px 16px 24px',
  };

  const bodyWrap: React.CSSProperties = {
    fontSize: '16px',
    lineHeight: '24px',
    color: '#111827',
  };

  const divider: React.CSSProperties = {
    height: '1px',
    backgroundColor: '#e5e7eb',
    margin: '18px 0',
  };

  const orgBlock: React.CSSProperties = {
    fontSize: '13px',
    lineHeight: '18px',
    color: '#4b5563',
  };

  const orgLine: React.CSSProperties = {
    margin: '0 0 4px 0',
    padding: 0,
  };

  const footer: React.CSSProperties = {
    backgroundColor: BRAND_BLUE,
    padding: '16px 18px',
    color: '#ffffff',
    textAlign: 'center',
  };

  const footerText: React.CSSProperties = {
    margin: '0 0 8px 0',
    fontSize: '13px',
    lineHeight: '18px',
  };

  const footerSmall: React.CSSProperties = {
    margin: 0,
    fontSize: '12px',
    lineHeight: '18px',
    opacity: 0.95,
  };

  const link: React.CSSProperties = {
    color: '#ffffff',
    textDecoration: 'underline',
    fontWeight: 700,
  };

  return (
    <html lang="es">
      <head>
        <meta charSet="UTF-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <meta name="x-apple-disable-message-reformatting" />
        <title>{data.title}</title>
      </head>

      <body style={page}>
        <table role="presentation" width="100%" style={outerTable}>
          <tbody>
            <tr>
              <td align="center" style={containerCell}>
                <table role="presentation" style={card}>
                  <tbody>
                    {/* Header */}
                    <tr>
                      <td style={header}>
                        <h1 style={title}>{data.title}</h1>
                      </td>
                    </tr>

                    {/* Main */}
                    <tr>
                      <td style={main}>
                        <div style={bodyWrap}>{data.body}</div>

                        <div style={divider} />

                        <div style={orgBlock}>
                          <p
                            style={{
                              ...orgLine,
                              fontWeight: 700,
                              color: '#374151',
                            }}
                          >
                            Hospital Santamaría Clisanta S.A.
                          </p>
                          <p style={orgLine}>
                            Dirección: Lorenzo de Garaicoa 3208 y Argentina
                          </p>
                          <p style={orgLine}>
                            Telf.: 2404650/2401767/2417824 EXT 501
                          </p>
                          <p style={orgLine}>www.hospitalsantamaria.com.ec</p>
                          <p style={orgLine}>
                            www.facebook.com/hospitalsantamariaec
                          </p>
                          <p style={{ ...orgLine, marginBottom: 0 }}>
                            @hospitalsantamaria
                          </p>
                        </div>
                      </td>
                    </tr>

                    {/* Footer */}
                    <tr>
                      <td style={footer}>
                        <p style={footerText}>
                          Copyright © {data.currentYear} Hospital Santamaría
                        </p>

                        <p style={footerSmall}>
                          Si esta información no es de su interés, envíenos un
                          correo en blanco a{' '}
                          <a
                            href="mailto:delegadolopdp@hospitalsantamaria.com.ec"
                            style={link}
                          >
                            delegadolopdp@hospitalsantamaria.com.ec
                          </a>{' '}
                          con el asunto <b>“REMOVER”</b>.
                        </p>

                        <p style={{ ...footerSmall, marginTop: '8px' }}>
                          O haciendo{' '}
                          <a
                            href="mailto:delegadolopdp@hospitalsantamaria.com.ec?subject=REMOVER"
                            style={link}
                          >
                            click aquí
                          </a>
                          .
                        </p>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </td>
            </tr>
          </tbody>
        </table>
      </body>
    </html>
  );
}
