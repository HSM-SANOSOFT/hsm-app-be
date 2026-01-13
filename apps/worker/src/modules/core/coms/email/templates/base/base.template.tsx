import { BaseTemplateDto } from '@hsm-lib/definitions/dtos';

export function BaseTemplate(data: BaseTemplateDto): React.ReactNode {
  const ul_Style: React.CSSProperties = {
    listStyleType: 'none',
    fontSize: 'medium',
    margin: 0,
    padding: 0,
  };
  const li_Style: React.CSSProperties = { margin: 0, padding: 0 };
  const body_Style: React.CSSProperties = {
    marginLeft: 'auto',
    marginRight: 'auto',
    width: '90%',
    maxWidth: '1300px',
    justifyContent: 'center',
    display: 'grid',
    fontFamily: 'sans-serif',
  };
  const card_Style: React.CSSProperties = {
    border: '1px solid black',
    borderRadius: '1rem',
    marginLeft: 'auto',
    marginRight: 'auto',
    overflow: 'hidden',
  };
  const h1_Style: React.CSSProperties = {
    textAlign: 'center',
    fontSize: '2em',
  };
  const col12_Style: React.CSSProperties = {
    paddingLeft: '15px',
    paddingRight: '15px',
  };
  const footer_Style: React.CSSProperties = {
    backgroundColor: '#06478d',
    color: 'white',
    padding: '1rem',
    borderTop: '1px solid black',
    textAlign: 'center',
  };
  const header_Style: React.CSSProperties = {
    backgroundColor: '#06478d',
    color: 'white',
    padding: '1rem',
    borderBottom: '1px solid black',
  };
  const a_Style: React.CSSProperties = {
    color: '#fff',
    textDecoration: 'none',
    fontWeight: 'bold',
  };
  return (
    <>
      <html lang="es">
        <head style={header_Style}>
          <meta charSet="UTF-8" />
          <meta
            name="viewport"
            content="width=device-width, initial-scale=1.0"
          />
          <style></style>
        </head>

        <body>
          <div className="body" style={body_Style}>
            <div className="card" style={card_Style}>
              <div className="row header">
                <div className="col-12" style={col12_Style}>
                  <h1 className="h1" style={h1_Style}>
                    {data.title}.
                  </h1>
                </div>
              </div>

              <div
                className="row main container"
                style={{ padding: '2rem', fontSize: 'large', display: 'grid' }}
              >
                <div className="col-12" style={col12_Style}>
                  <br />
                  <div>{data.body}</div>
                  <br />
                </div>

                <ul style={ul_Style}>
                  <li style={li_Style}>Hospital Santamaría Clisanta S.A.</li>
                  <li style={li_Style}>
                    Dirección: Lorenzo de Garaicoa 3208 y Argentina
                  </li>
                  <li style={li_Style}>
                    Telf.: 2404650/2401767/2417824 EXT 501
                  </li>
                  <li style={li_Style}>www.hospitalsantamaria.com.ec</li>
                  <li style={li_Style}>
                    www.facebook.com/hospitalsantamariaec
                  </li>
                  <li style={li_Style}>@hospitalsantamaria</li>
                </ul>
              </div>

              <div className="footer" style={footer_Style}>
                <p>Copyright © {data.currentYear} Hospital Santamaria</p>
                <p>Si esta información no es de su interes.</p>
                <p>
                  {' '}
                  Escribirnos un correo en blanco a{' '}
                  <b>
                    <a
                      href="mailto:delegadolopdp@hospitalsantamaria.com.ec"
                      style={a_Style}
                    >
                      {' '}
                      delegadolopdp@hospitalsantamaria.com.ec{' '}
                    </a>{' '}
                  </b>{' '}
                  con el Asunto "REMOVER".{' '}
                </p>
                <p>
                  {' '}
                  O haciendo{' '}
                  <a
                    href="mailto:delegadolopdp@hospitalsantamaria.com.ec?subject=REMOVER"
                    style={a_Style}
                  >
                    {' '}
                    Click Aquí{' '}
                  </a>{' '}
                </p>
              </div>
            </div>
          </div>
        </body>
      </html>
    </>
  );
}
