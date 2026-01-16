import { ResultadoExamenesTemplateDto } from '@hsm-lib/definitions/dtos';

export function ResultadoExamenesTemplate(
  data: ResultadoExamenesTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <b>{data.patientName}</b>
        <br />
        Le saludamos del Hospital Santamaria.
        <br />
        <br />
        Adjunto se encuentra los Resultados de los Examenes realizados en
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
