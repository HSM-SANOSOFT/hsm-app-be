import { SolicitudExamenesTemplateDto } from '@hsm-lib/definitions/dtos';

export function SolicitudExamenesTemplate(
  data: SolicitudExamenesTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <b>{data.patientName}</b>
        <br />
        Le saludamos del Hospital Santamaria.
        <br />
        <br />
        Adjunto se encuentra la Solicitud de Examenes para ser realizados en
        nuestro laboratorio.
        <br />
        <br />
        Atentamente
        <br />
        {data.labType}
        <br />
        Celular 0981764904
      </p>
    </>
  );
}
