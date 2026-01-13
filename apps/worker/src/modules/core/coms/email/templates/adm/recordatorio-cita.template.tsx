import { RecordatorioCitaTemplateDto } from '@hsm-lib/definitions/dtos';
export function RecordatorioCitaTemplate(
  data: RecordatorioCitaTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Le saludamos del Hospital Santamaría, le recordamos el paciente
        agendado:
      </p>
      <ul>
        <li>
          <strong>Nombre:</strong> {data.patientName}
        </li>
        <li>
          <strong>Fecha:</strong> {data.appointmentDate}
        </li>
        <li>
          <strong>Hora:</strong> {data.appointmentTime}
        </li>
      </ul>
      <p>
        {' '}
        Considere que la hora de su turno puede ajustarse según la
        disponibilidad, con el fin de optimizar su experiencia en el
        establecimiento. Le notificaremos con cualquier cambio.{' '}
      </p>
      <p>Atentamente</p>
      <p>Servicio al Paciente</p>
      <p>Celular 0981764904</p>
    </>
  );
}
