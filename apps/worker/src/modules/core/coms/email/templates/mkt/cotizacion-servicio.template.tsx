import { CotizacionServicioTemplateDto } from '@hsm-lib/definitions/dtos';

export function CotizacionServicioTemplate(
  data: CotizacionServicioTemplateDto,
): React.ReactNode {
  const buttonBaseStyle: React.CSSProperties = {
    width: 'auto',
    height: 'auto',
    fontSize: '15px',
    color: '#ffffff',
    padding: '5px',
    borderRadius: '5px',
    alignItems: 'center',
    whiteSpace: 'wrap',
  };
  const primaryButtonStyle: React.CSSProperties = {
    ...buttonBaseStyle,
    backgroundColor: '#06478d',
  };
  const dangerButtonStyle: React.CSSProperties = {
    ...buttonBaseStyle,
    backgroundColor: '#dc2626',
  };
  const buttonFlexStyle: React.CSSProperties = {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignContent: 'center',
    flexWrap: 'wrap',
  };

  return (
    <>
      <p>
        Estimado usuario:
        <br />
        <br />
        Se le notifica que aún tiene una cotización pendiente del servicio{' '}
        <b>{data.serviceName}</b>.
        <br />
        <br />
        Por favor seleccione el nivel de interés que tiene en continuar con el
        proceso:
      </p>
      <div style={buttonFlexStyle}>
        <a
          href="https://hospitalsm.org/sanosoft/api/crm_ventas/sel_interes.php?id_sol={{id_solicitud}}&nvl=I"
          style={primaryButtonStyle}
        >
          Estoy interesado
        </a>
        <a
          href="https://hospitalsm.org/sanosoft/api/crm_ventas/sel_interes.php?id_sol={{id_solicitud}}&nvl=N"
          style={dangerButtonStyle}
        >
          No estoy interesado
        </a>
      </div>
    </>
  );
}
